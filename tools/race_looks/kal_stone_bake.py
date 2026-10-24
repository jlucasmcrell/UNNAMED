"""Bake a scanned stone set into a HumGen body's own UV space, plus the Kal joint/age mask.

Triplanar projection in the body's bind-pose 3D space, so nothing breaks at UV island seams. Writes two textures the
runtime skin layer reads (kal_stone.gdshaderinc) and, for the baked delivery, the body's own albedo / roughness with the
stone composited at a fixed coverage.

usage: python kal_stone_bake.py <body.glb> <variant.json> <out dir> [--size 2048] [--coverage 0.55]
  variant.json: {"name", "colour", "height", "roughness", "ao", "tile_m", "tint": [r,g,b], "ao_strength"}
"""
import argparse
import io
import json
import struct
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage

Image.MAX_IMAGE_PIXELS = None
COMPONENT = {5120: np.int8, 5121: np.uint8, 5122: np.int16, 5123: np.uint16, 5125: np.uint32, 5126: np.float32}
WIDTH = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def read_glb(path):
    data = Path(path).read_bytes()
    length = struct.unpack_from("<I", data, 12)[0]
    gltf = json.loads(data[20:20 + length])
    blob = data[20 + length + 8:]

    def accessor(index):
        a = gltf["accessors"][index]
        view = gltf["bufferViews"][a["bufferView"]]
        dtype, width = COMPONENT[a["componentType"]], WIDTH[a["type"]]
        start = view.get("byteOffset", 0) + a.get("byteOffset", 0)
        stride = view.get("byteStride", 0) or np.dtype(dtype).itemsize * width
        raw = np.frombuffer(blob, np.uint8, count=stride * (a["count"] - 1) + np.dtype(dtype).itemsize * width, offset=start)
        rows = np.lib.stride_tricks.as_strided(raw, (a["count"], np.dtype(dtype).itemsize * width), (stride, 1))
        return np.ascontiguousarray(rows).view(dtype).reshape(a["count"], width).astype(np.float64 if dtype == np.float32 else dtype)

    def image(index):
        im = gltf["images"][index]
        view = gltf["bufferViews"][im["bufferView"]]
        return Image.open(io.BytesIO(blob[view.get("byteOffset", 0):view.get("byteOffset", 0) + view["byteLength"]]))

    return gltf, accessor, image


def body(path):
    gltf, accessor, image = read_glb(path)
    mesh = next(m for m in gltf["meshes"] if m["name"].endswith("Body_LOD0"))
    prim = mesh["primitives"][0]
    at = prim["attributes"]
    skin = gltf["skins"][0]
    names = [gltf["nodes"][i]["name"] for i in skin["joints"]]
    ibm = accessor(skin["inverseBindMatrices"]).reshape(-1, 4, 4).transpose(0, 2, 1)
    joints = {n: np.linalg.inv(m)[:3, 3] for n, m in zip(names, ibm)}
    material = gltf["materials"][prim["material"]]
    pbr = material["pbrMetallicRoughness"]
    tex = lambda t: image(gltf["textures"][t["index"]]["source"])
    return {
        "p": accessor(at["POSITION"]), "n": accessor(at["NORMAL"]), "uv": accessor(at["TEXCOORD_0"]),
        "j": accessor(at["JOINTS_0"]).astype(int), "w": accessor(at["WEIGHTS_0"]),
        "tri": accessor(prim["indices"]).reshape(-1, 3).astype(int), "names": names, "joints": joints,
        "albedo": tex(pbr["baseColorTexture"]), "mr": tex(pbr["metallicRoughnessTexture"]),
    }


def soft(d, r0, r1):
    return 1.0 - np.clip((d - r0) / (r1 - r0), 0.0, 1.0)


def seg_dist(p, a, b):
    ab = b - a
    t = np.clip(((p - a) @ ab) / (ab @ ab), 0.0, 1.0)
    return np.linalg.norm(p - (a + t[:, None] * ab), axis=1)


def affinity(b):
    """Per-vertex 0..1: how early a spot calcifies. ~1 = every adult Kal, ~0.3 = only the oldest, 0 = never."""
    p, n, J = b["p"], b["n"], b["joints"]
    up = np.array([0.0, 1.0, 0.0])
    fwd = np.sign(J["LeftEye"][2] - J["Head"][2]) * np.array([0.0, 0.0, 1.0])
    weight = np.zeros((len(p), len(b["names"])))
    np.put_along_axis(weight, b["j"], b["w"], axis=1)
    bone = lambda *names: sum(weight[:, b["names"].index(x)] for x in names)
    m = np.zeros(len(p))
    stature = J["Head"][1] - min(J["LeftFoot"][1], J["RightFoot"][1])
    s = stature / 1.45  # zone radii were tuned on a 1.5 m Kal

    for side in ("Left", "Right"):
        lat = np.array([1.0 if J[side + "UpperArm"][0] > 0 else -1.0, 0, 0])
        shoulder, elbow, wrist = J[side + "UpperArm"], J[side + "LowerArm"], J[side + "Hand"]
        knee, hip = J[side + "LowerLeg"], J[side + "UpperLeg"]
        # knuckles: the four proximal heads, back of the hand only
        for f in ("Index", "Middle", "Ring", "Little"):
            k = soft(np.linalg.norm(p - J[side + f + "Proximal"], axis=1), .012 * s, .024 * s) * (n @ up > .15)
            m = np.maximum(m, 1.0 * k)
        # back of the hand, the older tier
        m = np.maximum(m, .42 * bone(side + "Hand") * (n @ up > .3))
        # elbows and the dorsal forearm ridge (T-pose, palms down: dorsal = up)
        m = np.maximum(m, .98 * soft(np.linalg.norm(p - elbow, axis=1), .035 * s, .075 * s) * (n @ fwd < .35))
        ridge = soft(seg_dist(p, elbow, wrist), .03 * s, .065 * s) * np.clip((n @ up) * 1.6, 0, 1)
        along = np.clip(((p - wrist) @ (elbow - wrist)) / ((elbow - wrist) @ (elbow - wrist)), 0, 1)
        m = np.maximum(m, ridge * (.55 + .35 * along))
        # shoulder caps
        cap = soft(np.linalg.norm(p - shoulder - lat * .03 * s, axis=1), .05 * s, .11 * s) * np.clip(n @ ((lat + up) / np.sqrt(2)) * 1.5, 0, 1)
        m = np.maximum(m, .95 * cap)
        # collarbone: neck base to shoulder, front-top facing
        clav = soft(seg_dist(p, J["Neck"] + fwd * .03 * s - up * .03 * s, shoulder + fwd * .02 * s), .035 * s, .065 * s)
        m = np.maximum(m, .9 * clav * np.clip(n @ ((fwd + up) / np.sqrt(2)) * 1.6, 0, 1))
        # knees (front)
        m = np.maximum(m, .9 * soft(np.linalg.norm(p - knee - fwd * .04 * s, axis=1), .03 * s, .07 * s) * (n @ fwd > .2))
        # shins, the older tier
        shin = soft(seg_dist(p, knee, J[side + "Foot"] + up * .08 * s), .04 * s, .07 * s) * np.clip(n @ fwd * 1.5, 0, 1)
        m = np.maximum(m, .45 * shin)
        # outer thigh, the oldest tier
        m = np.maximum(m, .3 * bone(side + "UpperLeg") * np.clip(n @ lat * 1.4, 0, 1) * (p[:, 1] < hip[1] - .12 * s))
        # scapulae, older
        scap = soft(np.linalg.norm(p - (J["UpperChest"] * .4 + shoulder * .6) + fwd * .09 * s, axis=1), .05 * s, .1 * s) * (n @ fwd < -.4)
        m = np.maximum(m, .55 * scap)
        # ribs (side), oldest
        rib = soft(np.abs(p[:, 0] - lat[0] * .13 * s), .02 * s, .06 * s) * (np.abs(p[:, 1] - J["Chest"][1]) < .09 * s) * np.clip(n @ lat * 1.5, 0, 1)
        m = np.maximum(m, .33 * rib)
        # cheekbone and brow ridge, oldest
        cheek = soft(np.linalg.norm(p - (J[side + "Eye"] - up * .028 * s + lat * .022 * s + fwd * .005 * s), axis=1), .012 * s, .024 * s)
        brow = soft(np.linalg.norm(p - (J[side + "Eye"] + up * .022 * s + fwd * .012 * s), axis=1), .01 * s, .02 * s)
        m = np.maximum(m, .32 * np.maximum(cheek, brow) * (n @ fwd > .1))

    # spine ridge and the wing channels' raised rims either side of it, scapulae to sacrum
    back = n @ fwd < -.45
    span = (p[:, 1] > J["Hips"][1] + .04 * s) & (p[:, 1] < J["Neck"][1] + .02 * s)
    m = np.maximum(m, .97 * soft(np.abs(p[:, 0]), .012 * s, .03 * s) * back * span)
    rims = soft(np.abs(np.abs(p[:, 0]) - .085 * s), .01 * s, .028 * s) * back * span
    m = np.maximum(m, .78 * rims * (p[:, 1] > J["Spine"][1]))

    # never: underwear zone, palms and soles, nails/fingertips, the face centre, inside the mouth
    allowed = np.ones(len(p))
    hips = J["Hips"]
    brief = (p[:, 1] > hips[1] - .19 * s) & (p[:, 1] < hips[1] + .09 * s) & (np.abs(p[:, 0]) < .2 * s)
    m[brief] = 0
    allowed[brief] = 0
    if "LeftBreast" in J and np.linalg.norm(J["LeftBreast"] - J["RightBreast"]) > 0:
        for side in ("Left", "Right"):
            bra = np.linalg.norm(p - J[side + "Breast"], axis=1) < .12 * s
            m[bra] = 0
            allowed[bra] = 0
    tips = bone(*[sd + f + "Distal" for sd in ("Left", "Right") for f in ("Index", "Middle", "Ring", "Little", "Thumb")])
    palms = (bone("LeftHand", "RightHand") > .3) * (n @ up < -.2)
    soles = bone("LeftFoot", "RightFoot", "LeftToes", "RightToes") * (n @ up < -.5)
    face = soft(np.linalg.norm(p - (J["LeftEye"] + J["RightEye"]) / 2 + up * .035 * s, axis=1), .045 * s, .06 * s) * (n @ fwd > .2)
    mouth = bone("Jaw", "UpperJaw") * (n @ fwd > .5) * (p[:, 1] < J["LeftEye"][1] - .05 * s)
    m *= np.clip(1 - tips * 2, 0, 1)
    m *= np.clip(1 - bone("LeftFoot", "RightFoot", "LeftToes", "RightToes") * 1.5, 0, 1) * (1 - palms)
    m *= (1 - face) * (1 - mouth)
    allowed *= np.clip(1 - tips * 2.5, 0, 1) * (1 - palms) * np.clip(1 - soles * 2, 0, 1) * (1 - face) * (1 - mouth)
    return np.clip(m, 0, 1), np.clip(allowed, 0, 1)


def raster(b, values, size):
    """Interpolate per-vertex values (N, C) into UV space; returns (size, size, C) and a coverage mask."""
    uv = b["uv"] * size - .5
    out = np.zeros((size, size, values.shape[1]))
    hit = np.zeros((size, size), bool)
    for t in b["tri"]:
        a, c, d = uv[t]
        x0, y0 = np.floor(np.minimum(np.minimum(a, c), d)).astype(int)
        x1, y1 = np.ceil(np.maximum(np.maximum(a, c), d)).astype(int)
        x0, y0, x1, y1 = max(x0, 0), max(y0, 0), min(x1, size - 1), min(y1, size - 1)
        if x1 < x0 or y1 < y0:
            continue
        xs, ys = np.meshgrid(np.arange(x0, x1 + 1), np.arange(y0, y1 + 1))
        den = (c[1] - d[1]) * (a[0] - d[0]) + (d[0] - c[0]) * (a[1] - d[1])
        if abs(den) < 1e-12:
            continue
        l0 = ((c[1] - d[1]) * (xs - d[0]) + (d[0] - c[0]) * (ys - d[1])) / den
        l1 = ((d[1] - a[1]) * (xs - d[0]) + (a[0] - d[0]) * (ys - d[1])) / den
        l2 = 1 - l0 - l1
        inside = (l0 >= -1e-4) & (l1 >= -1e-4) & (l2 >= -1e-4)
        if not inside.any():
            continue
        v = l0[inside, None] * values[t[0]] + l1[inside, None] * values[t[1]] + l2[inside, None] * values[t[2]]
        out[ys[inside], xs[inside]] = v
        hit[ys[inside], xs[inside]] = True
    return out, hit


def dilate(img, hit, steps=12):
    idx = ndimage.distance_transform_edt(~hit, return_distances=False, return_indices=True)
    far = ndimage.distance_transform_edt(~hit) > steps
    filled = img[idx[0], idx[1]]
    filled[far] = img[far]
    return filled


def load(path, size=None):
    im = Image.open(path)
    im = im.convert("RGB") if im.mode not in ("L", "I;16", "I") else im.convert("F")
    a = np.asarray(im, np.float64)
    return a / 255.0 if a.max() > 1.5 and a.max() <= 255 else a / max(a.max(), 1.0)


def sample(tex, u, v):
    h, w = tex.shape[:2]
    x, y = (u % 1.0) * w, (v % 1.0) * h
    coords = np.stack([y.ravel(), x.ravel()])
    if tex.ndim == 2:
        return ndimage.map_coordinates(tex, coords, order=1, mode="wrap").reshape(u.shape)
    return np.stack([ndimage.map_coordinates(tex[..., c], coords, order=1, mode="wrap") for c in range(tex.shape[2])], -1).reshape(u.shape + (tex.shape[2],))


def triplanar(tex, pos, nrm, tile):
    w = np.abs(nrm) ** 4
    w /= w.sum(-1, keepdims=True)
    q = pos / tile
    acc = 0
    for axis, (a, b) in enumerate(((1, 2), (0, 2), (0, 1))):
        s = sample(tex, q[:, a] + .37 * axis, q[:, b] + .61 * axis)
        acc = acc + (s * w[:, axis, None] if s.ndim == 2 else s * w[:, axis])
    return acc


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("glb"); ap.add_argument("variant"); ap.add_argument("out")
    ap.add_argument("--size", type=int, default=2048); ap.add_argument("--coverage", type=float, default=.55)
    args = ap.parse_args()
    var = json.loads(Path(args.variant).read_text())
    out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
    b = body(args.glb)
    m, allowed = affinity(b)
    n = b["n"] / np.linalg.norm(b["n"], axis=1, keepdims=True)
    field, hit = raster(b, np.concatenate([b["p"], n, m[:, None], allowed[:, None]], 1), args.size)
    texels = hit.ravel()
    pos, nrm = field.reshape(-1, 8)[texels, :3], field.reshape(-1, 8)[texels, 3:6]
    nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-6)

    colour, height = load(var["colour"]), load(var["height"])
    rough, ao = load(var["roughness"]), load(var["ao"]) if var.get("ao") else None
    rough = rough if rough.ndim == 2 else rough[..., 1]
    height = height if height.ndim == 2 else height[..., 0]
    tile = var["tile_m"]
    col = triplanar(colour, pos, nrm, tile)
    hgt = triplanar(height, pos, nrm, tile)
    rgh = triplanar(rough, pos, nrm, tile)
    if ao is not None:
        ao = ao if ao.ndim == 2 else ao[..., 0]
        col *= (1 - var.get("ao_strength", .6) * (1 - triplanar(ao, pos, nrm, tile)))[:, None]
    col = np.clip(col * np.array(var.get("tint", [1, 1, 1])), 0, 1)
    lo, hi = np.percentile(hgt, [1, 99])
    hgt = np.clip((hgt - lo) / max(hi - lo, 1e-6), 0, 1)

    size = args.size
    def plane(values, ch):
        img = np.zeros((size * size, ch)); img[texels] = values.reshape(-1, ch)
        return dilate(img.reshape(size, size, ch), hit)
    rgbh = plane(np.concatenate([col, hgt[:, None]], 1), 4)
    mask = dilate(field[..., 6:7], hit)[..., 0]
    skin = dilate(field[..., 7:8], hit)[..., 0]
    rm = np.stack([plane(rgh[:, None], 1)[..., 0], mask, skin], -1)
    stem = var["name"]
    Image.fromarray((rgbh * 255 + .5).astype(np.uint8), "RGBA").save(out / f"{stem}_stone_rgbh.png")
    Image.fromarray((rm * 255 + .5).astype(np.uint8), "RGB").save(out / f"{stem}_stone_rm.png")

    # baked delivery: the body's own maps with the stone at a fixed coverage, by the same rule the shader uses
    base = np.asarray(b["albedo"].convert("RGB").resize((size, size), Image.LANCZOS), np.float64) / 255
    mr = np.asarray(b["mr"].convert("RGB").resize((size, size), Image.LANCZOS), np.float64) / 255
    t = 1 - args.coverage
    s = np.clip((mask + (rgbh[..., 3] - .5) * var.get("breakup", .45) - t) / .02 + .5, 0, 1)
    s *= mask > .02
    alb = base * (1 - s[..., None]) + rgbh[..., :3] * s[..., None]
    mr2 = mr.copy(); mr2[..., 1] = mr[..., 1] * (1 - s) + rm[..., 0] * s
    Image.fromarray((alb * 255 + .5).astype(np.uint8)).save(out / f"{stem}_baked_albedo.png")
    Image.fromarray((mr2 * 255 + .5).astype(np.uint8)).save(out / f"{stem}_baked_metal_rough.png")
    Image.fromarray((np.stack([mask] * 3, -1) * 255 + .5).astype(np.uint8)).save(out / f"{stem}_mask_preview.png")
    json.dump({"variant": var, "body": str(args.glb), "texels": int(texels.sum()), "coverage": args.coverage,
               "stone_fraction_at_coverage": float(s[hit].mean())}, open(out / f"{stem}_bake.json", "w"), indent=1)
    print("BAKED", stem, int(texels.sum()), round(float(s[hit].mean()), 3))


if __name__ == "__main__":
    main()
