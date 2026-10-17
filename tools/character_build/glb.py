"""A .glb read and rewritten in place: accessors as numpy arrays, written back into the same bytes (same count, same type), so every
texture, morph target, skin and extra of the original survives untouched. Shared by WP17's build tools."""
import json
import struct

import numpy as np

COMPONENT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class Glb:
    def __init__(self, path):
        raw = open(path, "rb").read()
        length = struct.unpack_from("<I", raw, 12)[0]
        self.doc = json.loads(raw[20:20 + length])
        at = 20 + length
        size = struct.unpack_from("<I", raw, at)[0]
        self.bin = bytearray(raw[at + 8:at + 8 + size])

    def _view(self, index):
        acc = self.doc["accessors"][index]
        view = self.doc["bufferViews"][acc["bufferView"]]
        dtype = np.dtype(COMPONENT[acc["componentType"]])
        width = WIDTH[acc["type"]]
        start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
        stride = view.get("byteStride", 0) or dtype.itemsize * width
        return np.ndarray((acc["count"], width), dtype=dtype, buffer=self.bin, offset=start, strides=(stride, dtype.itemsize))

    def read(self, index):
        return np.array(self._view(index), dtype=np.float64)

    def write(self, index, values):
        view = self._view(index)
        if values.shape != view.shape:
            raise ValueError(f"accessor {index}: shape {values.shape} != {view.shape}")
        view[...] = values.astype(view.dtype)
        acc = self.doc["accessors"][index]
        if "min" in acc:
            acc["min"] = [float(x) for x in values.min(0)]
            acc["max"] = [float(x) for x in values.max(0)]

    def node(self, name):
        return next(n for n in self.doc["nodes"] if n.get("name") == name)

    def primitives(self, name):
        node = self.node(name)
        return self.doc["meshes"][node["mesh"]]["primitives"]

    def save(self, path):
        js = json.dumps(self.doc, separators=(",", ":")).encode()
        js += b" " * (-len(js) % 4)
        bn = bytes(self.bin) + b"\0" * (-len(self.bin) % 4)
        with open(path, "wb") as f:
            f.write(struct.pack("<4sII", b"glTF", 2, 12 + 8 + len(js) + 8 + len(bn)))
            f.write(struct.pack("<I4s", len(js), b"JSON") + js)
            f.write(struct.pack("<I4s", len(bn), b"BIN\0") + bn)
