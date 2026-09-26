"""Read a GLB's first mesh (every primitive concatenated) and its embedded images into numpy, exactly as stored."""
import io
import json
import struct

import numpy as np
from PIL import Image

_TYPES = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
_WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


class Glb:
    def __init__(self, path):
        b = open(path, "rb").read()
        n = struct.unpack("<I", b[12:16])[0]
        self.json = json.loads(b[20:20 + n])
        self.bin = b[20 + n + 8:]

    def accessor(self, index):
        a = self.json["accessors"][index]
        view = self.json["bufferViews"][a["bufferView"]]
        dtype, width = _TYPES[a["componentType"]], _WIDTH[a["type"]]
        stride = view.get("byteStride", 0)
        offset = view.get("byteOffset", 0) + a.get("byteOffset", 0)
        item = np.dtype(dtype).itemsize * width
        if stride and stride != item:
            raw = np.frombuffer(self.bin, np.uint8, count=stride * (a["count"] - 1) + item, offset=offset)
            rows = np.lib.stride_tricks.as_strided(raw, (a["count"], item), (stride, 1))
            out = rows.copy().view(dtype).reshape(a["count"], width)
        else:
            out = np.frombuffer(self.bin, dtype, count=a["count"] * width, offset=offset).reshape(a["count"], width).copy()
        if a.get("normalized") and dtype != np.float32:
            out = out.astype(np.float32) / np.iinfo(dtype).max
        return out if width > 1 else out[:, 0]

    def mesh(self, index=0):
        """positions, normals, uv, faces (and joints/weights when skinned), all primitives in one set."""
        parts = {k: [] for k in ("POSITION", "NORMAL", "TEXCOORD_0", "JOINTS_0", "WEIGHTS_0", "TANGENT", "faces", "material")}
        base = 0
        for p in self.json["meshes"][index]["primitives"]:
            at = p["attributes"]
            count = self.json["accessors"][at["POSITION"]]["count"]
            for k in ("POSITION", "NORMAL", "TEXCOORD_0", "JOINTS_0", "WEIGHTS_0", "TANGENT"):
                if k in at:
                    parts[k].append(self.accessor(at[k]))
            faces = self.accessor(p["indices"]).reshape(-1, 3).astype(np.int64) + base
            parts["faces"].append(faces)
            parts["material"].append(np.full(len(faces), p.get("material", -1)))
            base += count
        return {k: (np.concatenate(v) if v else None) for k, v in parts.items()}

    def image(self, index):
        im = self.json["images"][index]
        view = self.json["bufferViews"][im["bufferView"]]
        o = view.get("byteOffset", 0)
        return Image.open(io.BytesIO(self.bin[o:o + view["byteLength"]]))

    def texture_image(self, texture_index):
        return self.image(self.json["textures"][texture_index]["source"])
