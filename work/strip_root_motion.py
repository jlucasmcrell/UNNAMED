"""Strip baked root motion from creature clips whose pelvis translation travels metres.

A creature the game moves itself must animate in place. Where a clip carries the walk/run travel in the pelvis translation, the animal slides
inside its own transform and snaps back on loop; where that travel is on Y (a Z-up export that was only corrected on the root rotation) it also
lifts the body off the ground, which is what the Upland Elk does.

This finds every clip in the ready set whose pelvis-style translation channel travels more than the threshold, reports it, and - with --fix -
rewrites that channel to a constant taken from the clip's own first key. Rotations are untouched, so the gait survives.
"""
import argparse
import re
import json
import shutil
import struct
from pathlib import Path

READY = Path(r'D:\Otherreach_Shared\art_root\animation\ready\creatures')
BACKUP = READY.parent / 'creatures_backup_rootmotion'
THRESHOLD_M = 0.35


def load(path):
    data = path.read_bytes()
    offset, doc, binary, chunks = 12, None, b'', []
    while offset < len(data):
        length, kind = struct.unpack_from('<II', data, offset)
        payload = data[offset + 8:offset + 8 + length]
        chunks.append((kind, offset + 8, length))
        if kind == 0x4E4F534A:
            doc = json.loads(payload.decode('utf-8'))
        elif kind == 0x004E4942:
            binary = payload
        offset += 8 + length
    return data, doc, binary, chunks


def read_accessor(doc, binary, accessor):
    view = doc['bufferViews'][accessor['bufferView']]
    start = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    width = {'SCALAR': 1, 'VEC3': 3, 'VEC4': 4}[accessor['type']]
    stride = view.get('byteStride') or width * 4
    return [list(struct.unpack_from('<' + 'f' * width, binary, start + i * stride)) for i in range(accessor['count'])], start, stride, width


def travel(doc, binary, animation, channel):
    values, _, _, _ = read_accessor(doc, binary, doc['accessors'][animation['samplers'][channel['sampler']]['output']])
    if not values:
        return 0.0, None
    lows = [min(v[i] for v in values) for i in range(3)]
    highs = [max(v[i] for v in values) for i in range(3)]
    spread = [highs[i] - lows[i] for i in range(3)]
    return max(spread), spread


def report(fix):
    BACKUP.mkdir(exist_ok=True)
    offenders = []
    for clip in sorted(READY.glob('*.glb')):
        data, doc, binary, chunks = load(clip)
        changed = False
        for animation in doc.get('animations', []):
            for channel in animation['channels']:
                if channel['target']['path'] != 'translation':
                    continue
                worst, spread = travel(doc, binary, animation, channel)
                if worst is None or worst <= THRESHOLD_M:
                    continue
                node = doc['nodes'][channel['target']['node']].get('name', '?')
                if not re.search(r'pelvis|hips|^root', node, re.I):
                    continue
                offenders.append((clip.name, node, worst, spread))
                if not fix:
                    continue
                accessor_ref = animation['samplers'][channel['sampler']]['output']
                values, start, stride, width = read_accessor(doc, binary, doc['accessors'][accessor_ref])
                # Hold the first key: the pose the clip opens in is the one the creature stands in.
                held = values[0]
                for i in range(len(values)):
                    struct.pack_into('<' + 'f' * width, buffer := bytearray(binary), start + i * stride, *held)
                    binary = bytes(buffer)
                changed = True
        if changed:
            shutil.copy2(clip, BACKUP / clip.name)
            write(clip, doc, binary)
    return offenders


def write(path, doc, binary):
    """Rewrite the GLB with the same JSON (byte offsets unchanged: only float values were replaced)."""
    new_json = json.dumps(doc, separators=(',', ':')).encode('utf-8')
    new_json += b' ' * ((4 - len(new_json) % 4) % 4)
    original = path.read_bytes()
    offset, chunks = 12, []
    while offset < len(original):
        length, kind = struct.unpack_from('<II', original, offset)
        chunks.append((kind, original[offset + 8:offset + 8 + length]))
        offset += 8 + length
    out = bytearray()
    body = bytearray()
    for kind, payload in chunks:
        payload = new_json if kind == 0x4E4F534A else binary
        body += struct.pack('<II', len(payload), kind) + payload
    out += struct.pack('<III', 0x46546C67, 2, 12 + len(body))
    out += body
    path.write_bytes(bytes(out))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--fix', action='store_true')
    args = parser.parse_args()
    found = report(args.fix)
    print(f'{len(found)} clip channel(s) travel more than {THRESHOLD_M} m')
    for name, node, worst, spread in found[:20]:
        print(f'  {name:<48} {node:<14} travel {worst:5.2f} m  (x {spread[0]:.2f}, y {spread[1]:.2f}, z {spread[2]:.2f})')
    if args.fix:
        print(f'originals backed up to {BACKUP}')
