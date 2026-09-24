"""Report the world-space bounding box of a GLB, reading accessor min/max from the glTF header.

The glTF POSITION accessors carry `min` and `max`, so a bounding box needs no buffer decoding and no
Blender. That matters here because the rescale tool rewrites accessor data in place, and the only way
to know it worked - and to know whether a *rigged* copy was left behind at the old size - is to read
the numbers back out.

For a skinned mesh the reported box is the bind-pose geometry, which is what the rescale tool refuses
to touch. That refusal is correct: scaling skinned vertices alone would leave the inverse bind
matrices expressed in the old scale. So this tool reports the discrepancy rather than fixing it.

Usage:
    python _glb_bounds.py path/to/a.glb [more.glb ...]
"""
import json
import os
import struct
import sys


def read_glb(path):
    with open(path, "rb") as handle:
        data = handle.read()
    if len(data) < 12:
        return None, "too short"
    magic, version, _length = struct.unpack_from("<4sII", data, 0)
    if magic != b"glTF":
        return None, "not a GLB container"
    offset = 12
    header = None
    while offset + 8 <= len(data):
        chunk_length, chunk_type = struct.unpack_from("<II", data, offset)
        body = data[offset + 8:offset + 8 + chunk_length]
        if chunk_type == 0x4E4F534A:      # JSON
            header = json.loads(body.decode("utf-8"))
            break
        offset += 8 + chunk_length + ((4 - chunk_length % 4) % 4)
    if header is None:
        return None, "no JSON chunk"
    return header, None


def bounds(header):
    """Union of every POSITION accessor's min/max, in the file's own coordinates."""
    low = [float("inf")] * 3
    high = [float("-inf")] * 3
    found = 0
    for mesh in header.get("meshes", []):
        for primitive in mesh.get("primitives", []):
            index = (primitive.get("attributes") or {}).get("POSITION")
            if index is None:
                continue
            accessor = header["accessors"][index]
            if "min" not in accessor or "max" not in accessor:
                continue
            found += 1
            for axis in range(3):
                low[axis] = min(low[axis], float(accessor["min"][axis]))
                high[axis] = max(high[axis], float(accessor["max"][axis]))
    if not found:
        return None
    return low, high


def main():
    for path in sys.argv[1:]:
        header, error = read_glb(path)
        if error:
            print(f"  {os.path.basename(path):<52} ERROR {error}")
            continue
        box = bounds(header)
        if box is None:
            print(f"  {os.path.basename(path):<52} no POSITION accessors")
            continue
        low, high = box
        dims = [high[i] - low[i] for i in range(3)]
        skins = len(header.get("skins", []))
        print(f"  {os.path.basename(path):<52} "
              f"dims {dims[0]:.3f} x {dims[1]:.3f} x {dims[2]:.3f} m  "
              f"longest {max(dims):.3f}  skins={skins}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
