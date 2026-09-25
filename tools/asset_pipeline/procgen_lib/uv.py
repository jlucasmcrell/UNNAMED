"""Atlas UVs: member-frame islands packed at one texel density, with padding, optionally into horizontal bands.

The skyline packer and the densest-fit search come from _procgen_crate.py (skyline_pack, layout_islands,
island_st_to_px); the shelf packer from _procgen_shaft.py (shelf_pack). Bands replace the winch's hand-placed band
rows: every island is packed inside its band, and check_bands() measures the final UVs against the bands, so an
island that overshoots its band (the winch bug: v offsets drawn without the island's own height) stops the build.

Islands come from the Builder: each face carries (island key, corner coordinates in metres of that island's own
plane, from the primitive's member-frame unwrap); an island's texel scale multiplies the density (e.g. 0.1 for faces
nobody sees, 2.0 for small fixings).

API
    frame_unwrap(b) -> list of island dicts {key, faces, scale, band, smin, tmin, ext}
    skyline_pack(sizes, W, H, gap=1) -> [(x, y, turned)] | None
    shelf_pack(sizes, W, H, gap=1) -> [(x, y, turned)] | None
    layout(islands, res, px_per_m=512, margin_px=3, bands=None, method='skyline', min_px_per_m=40) -> stats
        bands: {name: (y0, y1)} fractions of the atlas height; an island's band is its 'band' key (default: the
        first band). All bands share one density: the densest at which every band fits.
    loop_uvs(b, islands, res, margin_px) -> (n_loops, 2) UVs in face corner order
    check_bands(b, islands, loop_uv, res, bands, margin_px) -> report (islands_outside, worst_overshoot_px)
    pack(b, res, px_per_m=512, margin_px=3, bands=None, band_of=None, method='skyline') -> (loop_uv, stats)
        the whole step: unwrap, layout, UVs, band check (raises on an overshoot)
    measured_density(me, res, full_scale_mask=None) -> px/m measured on the triangulated mesh
    builder_density(b, loop_uv, res) -> px/m measured over the Builder's full-scale (texel 1.0) faces
"""
import math

import numpy as np


def frame_unwrap(b, band_of=None):
    """Islands from the Builder's per-face island keys and member-frame corner UVs (metres)."""
    islands = []
    for key, isl in b.islands().items():
        pts = np.concatenate([np.asarray(b.fuv[fi], np.float64) for fi in isl["faces"]])
        smin, tmin = pts.min(axis=0)
        smax, tmax = pts.max(axis=0)
        band = band_of(b.parts[key[0]], key[1]) if band_of else None
        islands.append({"key": key, "faces": isl["faces"], "scale": isl["scale"], "band": band,
                        "smin": float(smin), "tmin": float(tmin),
                        "ext": (max(float(smax - smin), 1e-5), max(float(tmax - tmin), 1e-5))})
    return islands


def skyline_pack(sizes, W, H, gap=1):
    """Skyline packing of (w, h) rects in the given order, each placed (upright or turned 90 deg) where its top ends
    lowest. Returns [(x, y, turned)] or None when they do not fit."""
    sky = np.zeros(W, np.int64)
    out = []
    for w, h in sizes:
        best = None
        for turned, (rw, rh) in ((False, (w, h)), (True, (h, w))):
            if rw + 2 * gap > W or (turned and w == h):
                continue
            win = np.lib.stride_tricks.sliding_window_view(sky, rw + 2 * gap).max(axis=1)
            x = int(np.argmin(win + rh))
            cand = (int(win[x]) + rh, int(win[x]), x, turned, rw, rh)
            if best is None or cand[:3] < best[:3]:
                best = cand
        if best is None:
            return None
        top, y, x, turned, rw, rh = best
        if top + 2 * gap > H:
            return None
        sky[x:x + rw + 2 * gap] = y + rh + gap
        out.append((x + gap, y + gap, turned))
    return out


def shelf_pack(sizes, W, H, gap=1):
    """Shelf packing in the given order (sort tallest first for a tight result); islands taller than wide are turned.
    Returns [(x, y, turned)] or None."""
    x = y = shelf = 0
    out = []
    for w, h in sizes:
        turned = h > w
        rw, rh = (h, w) if turned else (w, h)
        if rw + 2 * gap > W:
            return None
        if x + rw + 2 * gap > W:
            y += shelf
            x = shelf = 0
        if y + rh + 2 * gap > H:
            return None
        out.append((x + gap, y + gap, turned))
        x += rw + 2 * gap
        shelf = max(shelf, rh + 2 * gap)
    return out


def _band_px(bands, name, res):
    y0, y1 = bands[name]
    return int(round(y0 * res)), int(round(y1 * res))


def layout(islands, res, px_per_m=512, margin_px=3, bands=None, method="skyline", min_px_per_m=40):
    """Densest texel density (px/m, up to px_per_m) at which every island fits in its band, trying a few orders.
    Writes px (x, y), turned and d (px/m for this island) into each island."""
    bands = bands or {"all": (0.0, 1.0)}
    first = next(iter(bands))
    for isl in islands:
        if isl.get("band") is None:
            isl["band"] = first
        assert isl["band"] in bands, f"island {isl['key']} names an unknown band {isl['band']}"
    M = margin_px
    packer = skyline_pack if method == "skyline" else shelf_pack
    keys = {
        "height": lambda i: (-i["ext"][1] * i["scale"], -i["ext"][0] * i["scale"]),
        "long_side": lambda i: (-max(i["ext"]) * i["scale"], -min(i["ext"]) * i["scale"]),
        "area": lambda i: (-i["ext"][0] * i["ext"][1] * i["scale"] ** 2,),
    }

    def sizes_for(group, dens):
        return [(int(math.ceil(i["ext"][0] * dens * i["scale"])) + 2 * M,
                 int(math.ceil(i["ext"][1] * dens * i["scale"])) + 2 * M) for i in group]

    def attempt(orders, dens):
        placed = {}
        for name, group in orders.items():
            y0, y1 = _band_px(bands, name, res)
            got = packer(sizes_for(group, dens), res, y1 - y0)
            if got is None:
                return None
            placed[name] = got
        return placed

    best = None
    for oname, key in keys.items():
        orders = {}
        for name in bands:
            group = [i for i in islands if i["band"] == name]
            orders[name] = sorted(group, key=lambda i: key(i) + (str(i["key"]),))
        lo, hi = float(min_px_per_m), float(px_per_m)
        if attempt(orders, lo) is None:
            continue
        if attempt(orders, hi) is not None:
            lo = hi
        while hi - lo >= 0.5:
            mid = (lo + hi) / 2
            if attempt(orders, mid) is not None:
                lo = mid
            else:
                hi = mid
        dens = math.floor(lo)
        if best is None or dens > best[0]:
            best = (dens, oname, orders)
    if best is None:
        raise SystemExit(f"UV layout: the islands do not fit a {res} atlas even at {min_px_per_m} px/m")
    dens, oname, orders = best
    placed = attempt(orders, dens)
    used = 0
    for name, group in orders.items():
        y0, _ = _band_px(bands, name, res)
        for (w, h), (x, y, turned), isl in zip(sizes_for(group, dens), placed[name], group):
            isl["px"] = (x, y + y0)
            isl["turned"] = turned
            isl["d"] = dens * isl["scale"]
            used += w * h
    return {"px_per_m": dens, "atlas_use": round(used / float(res * res), 4), "pack_order": oname,
            "method": method, "islands": len(islands), "margin_px": M, "bands": bands}


def _st_to_px(isl, s, t, M):
    """Island (s, t) in metres -> atlas pixels. A turned island is rotated +90 deg (still right-handed)."""
    x0, y0 = isl["px"]
    if isl["turned"]:
        tmax = isl["tmin"] + isl["ext"][1]
        return x0 + M + (tmax - t) * isl["d"], y0 + M + (s - isl["smin"]) * isl["d"]
    return x0 + M + (s - isl["smin"]) * isl["d"], y0 + M + (t - isl["tmin"]) * isl["d"]


def loop_uvs(b, islands, res, margin_px):
    by_face = {}
    for isl in islands:
        for fi in isl["faces"]:
            by_face[fi] = isl
    out = []
    for fi, corners in enumerate(b.fuv):
        isl = by_face[fi]
        st = np.asarray(corners, np.float64)
        px, py = _st_to_px(isl, st[:, 0], st[:, 1], margin_px)
        out.append(np.c_[px, py] / res)
    return np.concatenate(out)


def check_bands(b, islands, loop_uv, res, bands, margin_px):
    """Every island's final UVs (in px) must lie inside the atlas and inside its band, at least margin_px/2 from
    the band edges (the bake's margin fill must not reach into a neighbouring band)."""
    bands = bands or {"all": (0.0, 1.0)}
    starts = np.cumsum([0] + [len(f) for f in b.fuv])
    uvpx = np.asarray(loop_uv, np.float64) * res
    report = {"islands_outside": 0, "worst_overshoot_px": 0.0, "examples": []}
    for isl in islands:
        pts = np.concatenate([uvpx[starts[fi]:starts[fi + 1]] for fi in isl["faces"]])
        y0, y1 = _band_px(bands, isl["band"], res)
        lim = margin_px / 2.0
        over = max(lim - pts[:, 0].min(), pts[:, 0].max() - (res - lim), (y0 + lim) - pts[:, 1].min(),
                   pts[:, 1].max() - (y1 - lim), 0.0)
        if over > 0:
            report["islands_outside"] += 1
            report["worst_overshoot_px"] = max(report["worst_overshoot_px"], round(float(over), 2))
            if len(report["examples"]) < 6:
                report["examples"].append(f"island {isl['key']} leaves band {isl['band']} by {over:.1f} px")
    report["ok"] = report["islands_outside"] == 0
    return report


def pack(b, res, px_per_m=512, margin_px=3, bands=None, band_of=None, method="skyline"):
    """Unwrap, lay out, emit UVs and check the bands; raises SystemExit if an island leaves its band."""
    islands = frame_unwrap(b, band_of)
    stats = layout(islands, res, px_per_m, margin_px, bands, method)
    loop_uv = loop_uvs(b, islands, res, margin_px)
    check = check_bands(b, islands, loop_uv, res, bands, margin_px)
    stats["band_check"] = {k: v for k, v in check.items() if k != "examples"}
    if not check["ok"]:
        raise SystemExit(f"UV islands outside their bands: {check}")
    return loop_uv, stats


def measured_density(me, res, full_scale_mask=None):
    """Texel density measured on the mesh: sqrt(UV area / 3D area) * res over the chosen triangles."""
    from .validate import mesh_checks
    a3, a2 = mesh_checks(me)
    if full_scale_mask is not None:
        a3, a2 = a3[full_scale_mask], a2[full_scale_mask]
    return round(math.sqrt(a2.sum() / max(a3.sum(), 1e-12)) * res, 1)


def builder_density(b, loop_uv, res):
    """sqrt(UV area / 3D area) * res over the faces whose island texel scale is 1.0."""
    V = np.asarray(b.verts, np.float64)
    uvs = np.asarray(loop_uv, np.float64)
    off, a2, a3 = 0, 0.0, 0.0
    for f, key in zip(b.faces, b.fisland):
        n = len(f)
        if b.island_scale[key] == 1.0:
            P, U = V[list(f)], uvs[off:off + n]
            a3 += np.linalg.norm(np.sum(np.cross(P, np.roll(P, -1, axis=0)), axis=0)) / 2
            a2 += abs(np.sum(U[:, 0] * np.roll(U[:, 1], -1) - np.roll(U[:, 0], -1) * U[:, 1])) / 2
        off += n
    return round(math.sqrt(a2 / max(a3, 1e-12)) * res, 1)
