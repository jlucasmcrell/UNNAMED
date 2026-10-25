"""Place the Spill's ruin pieces (docs/FIELD_RUINS.md): region structures, switches, the readable and their art bindings.

Each piece's footprint is measured from its converted GLB (D:/Otherreach_Depot/field_ruins/ready/<id>/<id>.glb): the XZ convex
hull (bevels dropped) becomes the structure's hull_m, so a diagonal beam blocks the beam and not the empty triangle beside
it, and its height_m is the model's own. Fitting centres a model's box on the hull's corner average; where the rectangle is not
centred on the model's box the binding's offset moves the model back, so the drawn piece and the collision are the same place.

  python tools/field_ruins/place_spill.py [--preview out.png]

The region's structures between the FIELD_RUINS markers, the switches and readable, and the ruin_fs_* bindings are rewritten on every
run, so the layout below is the one source of truth.
"""
import argparse
import json
import math
import os
import struct
import sys

import numpy as np
from scipy.spatial import ConvexHull

DEPOT = "D:/Otherreach_Depot/field_ruins/ready"
REGION = "content/regions/starting_frontier.yaml"
BINDINGS = "content/_art_bindings.json"
BEGIN = "# FIELD_RUINS BEGIN - written by tools/field_ruins/place_spill.py; edit the layout there, not here."
END = "# FIELD_RUINS END"

# (structure id, asset, x, z of the model's box centre in world metres, material, extra binding keys)
# Otherfire is violet at the Spill (owner, 2026-10-24: the colour of anything that bends reality); it goes out when its switch is set.
VIOLET = {"light": {"at": [0, 5.5, 0], "colour": [0.62, 0.3, 1.0], "energy": 3.0, "range": 16, "shadows": False}, "set_goes_dark": True}
NEEDLE_FIRE = {"light": {"at": [-14, 4, 0], "colour": [0.62, 0.3, 1.0], "energy": 4.0, "range": 26, "shadows": False}, "set_goes_dark": True}


def iron(tint):
    """Firstfold iron worn over a piece's own relief (make_firstfold_iron.py), its edges chipped, each kind of piece its own tone."""
    return {"surface": "material_firstfold_iron", "surface_tint": tint, "edge_wear": 0.8}


# The east lane runs between the north rib and the Needle, and at noon the Needle's shade fills it. The sun's bounce off the lit iron
# and grass is put back by a fill hung off the rib (not the Needle, whose lights go out with its switch), as strong as the day.
LANE_BOUNCE = {"light_world": [890.0, 9.0, 729.5], "light": {"colour": [1.0, 0.96, 0.88], "energy": 1.6, "range": 30, "shadows": False,
                                                          "daylight": -1}}

LAYOUT = [
    ("fs_needle", "ruin_fs_needle", 890.0, 752.0, "metal", NEEDLE_FIRE | iron([0.82, 0.84, 0.9])),
    ("fs_gate", "ruin_fs_gate", 850.0, 758.0, "metal", {}),
    ("fs_anchor_west", "ruin_fs_anchor", 836.0, 760.0, "metal", VIOLET),
    ("fs_anchor_northeast", "ruin_fs_anchor", 858.5, 745.0, "metal", VIOLET),
    ("fs_anchor_south", "ruin_fs_anchor", 853.0, 771.5, "metal", VIOLET),
    ("fs_panel", "ruin_fs_panel", 846.0, 737.0, "stone", {}),
    ("fs_colonnade", "ruin_fs_colonnade", 834.0, 730.5, "metal", iron([1.12, 1.08, 1.02])),
    ("fs_frame_north", "ruin_fs_frame", 821.0, 744.0, "metal", iron([1.12, 1.08, 1.02])),
    ("fs_frame_south", "ruin_fs_frame", 821.0, 770.0, "metal", iron([1.12, 1.08, 1.02])),
    ("fs_slab", "ruin_fs_slab", 834.0, 701.0, "stone", iron([1.06, 1.0, 0.94])),
    ("fs_block", "ruin_fs_block", 814.0, 786.0, "stone", iron([1.06, 1.0, 0.94])),
    ("fs_rib_north", "ruin_fs_rib_a", 893.0, 699.0, "metal", iron([0.92, 0.96, 1.06]) | LANE_BOUNCE),
    ("fs_rib_east", "ruin_fs_rib_c", 897.0, 784.4, "metal", iron([0.92, 0.96, 1.06])),
    ("fs_pylon_a", "ruin_fs_pylon_a", 862.0, 718.0, "metal", iron([1.0, 1.0, 1.0])),
    ("fs_pylon_b", "ruin_fs_pylon_b", 868.0, 790.0, "metal", iron([1.0, 1.0, 1.0])),
    ("fs_pylon_c", "ruin_fs_pylon_c", 842.0, 790.0, "metal", iron([1.0, 1.0, 1.0])),
    ("fs_stub_a", "ruin_fs_stub_a", 832.0, 750.0, "metal", iron([0.95, 0.95, 0.97])),
    ("fs_stub_b", "ruin_fs_stub_b", 868.0, 772.0, "metal", iron([0.95, 0.95, 0.97])),
    ("fs_stub_c", "ruin_fs_stub_c", 840.0, 716.0, "metal", iron([0.95, 0.95, 0.97])),
]

SWITCHES = [
    ("switch.field.anchor_west", "fs_anchor_west", "world.field.anchor_west_stilled", "west anchor", "Still", None,
     "The drum's violet light gutters and goes out. The hum round it drops a tone, and then another, and stops."),
    ("switch.field.anchor_northeast", "fs_anchor_northeast", "world.field.anchor_northeast_stilled", "north-east anchor", "Still", None,
     "Under your hand the drum stops pulling. The light in it sinks to a coal and then to nothing."),
    ("switch.field.anchor_south", "fs_anchor_south", "world.field.anchor_south_stilled", "south anchor", "Still", None,
     "The anchor lets go with a sound like a breath let out. The court is a note quieter."),
    ("switch.field.needle", "fs_needle", "world.field.stilled", "foot of the Needle", "Lay a hand on",
     ["world.field.anchor_west_stilled", "world.field.anchor_northeast_stilled", "world.field.anchor_south_stilled"],
     "The iron is warm, and then it is not. Far above you something settles, a long way down into itself, and the hum you stopped hearing an hour ago is gone. The gate is shut."),
]
NEEDLE_LOCKED = "The iron under your hand pulls against something. Somewhere in the court an anchor is still humming."
# The keepers (tools/field_ruins/build_keepers.py) play the golem's own clips: the rig is the golem's, scaled, and Retarget carries the
# keys over by bone name. Paces are the golem's authored walk (1.88 m/s at 4 m) and run (6.1) times each keeper's height over 4 m.
GOLEM_CLIPS = {s: f"creature.creature_golem_stan.{s}" for s in ("idle", "walk", "run", "attack", "hit", "death")}
CREATURES = {
    "creature.construct.gate_tender": {
        "model": "creature_gate_tender", "clips": GOLEM_CLIPS | {"ability.creature.tender_strike": GOLEM_CLIPS["attack"]},
        "voice": "animated_armour", "flesh": "plate", "steps": {"walk": "walk", "run": "move_heavy"},
        "paces": {"walk": 1.41, "run": 4.58}},
    "creature.construct.gate_warden": {
        "model": "creature_gate_warden",
        "clips": GOLEM_CLIPS | {f"ability.creature.warden_{a}": GOLEM_CLIPS["attack"] for a in ("sweep", "fall", "grasp")},
        "voice": "animated_armour", "flesh": "plate", "steps": {"walk": "move_heavy", "run": "move_heavy"},
        "paces": {"walk": 3.76, "run": 12.2}},
}
# Round pieces stand on a circle_m: a drum's or a disc's own outline, not the diamond its square hull would make.
ROUND = {"ruin_fs_gate", "ruin_fs_anchor"}
PANEL = ("readable.stopped_gate_panel", "lore.stopped_gate", 1, "panel of marks", "Read")


def glb_points(path):
    """Every vertex of the GLB in its own frame (node transforms applied), as an N x 3 array."""
    data = open(path, "rb").read()
    magic, _, _ = struct.unpack_from("<III", data, 0)
    assert magic == 0x46546C67, path
    jlen, _ = struct.unpack_from("<II", data, 12)
    doc = json.loads(data[20:20 + jlen])
    bin_start = 20 + jlen + 8
    out = []

    def matrix(node):
        if "matrix" in node:
            return np.array(node["matrix"], dtype=float).reshape(4, 4).T
        t = np.eye(4)
        x, y, z, w = node.get("rotation", [0, 0, 0, 1])
        r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                      [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                      [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
        t[:3, :3] = r * np.array(node.get("scale", [1, 1, 1]))
        t[:3, 3] = node.get("translation", [0, 0, 0])
        return t

    def walk(index, parent):
        node = doc["nodes"][index]
        m = parent @ matrix(node)
        if "mesh" in node:
            for prim in doc["meshes"][node["mesh"]]["primitives"]:
                acc = doc["accessors"][prim["attributes"]["POSITION"]]
                view = doc["bufferViews"][acc["bufferView"]]
                offset = bin_start + view.get("byteOffset", 0) + acc.get("byteOffset", 0)
                stride = view.get("byteStride", 12)
                raw = np.frombuffer(data, dtype=np.uint8, count=stride * acc["count"], offset=offset).reshape(acc["count"], stride)
                pts = raw[:, :12].copy().view(np.float32).reshape(-1, 3).astype(float)
                out.append((m @ np.c_[pts, np.ones(len(pts))].T).T[:, :3])
        for child in node.get("children", []):
            walk(child, m)

    for root in doc["scenes"][doc.get("scene", 0)]["nodes"]:
        walk(root, np.eye(4))
    return np.vstack(out)


def hull(xz):
    """The points' convex hull, counter-clockwise, with corners closer than 0.25 m to the hull through their neighbours dropped: the
    footprint keeps the model's own box (Fitting holds the two within half a metre a side) without a corner for every bevel."""
    pts = xz[ConvexHull(xz).vertices]
    keep = list(pts)
    changed = True
    while changed and len(keep) > 4:
        changed = False
        for i in range(len(keep)):
            a, b, c = keep[i - 1], keep[i], keep[(i + 1) % len(keep)]
            ab, ac = b - a, c - a
            gap = abs(ab[0] * ac[1] - ab[1] * ac[0]) / max(np.hypot(*ac), 1e-6)
            on_box = any(abs(b[k] - e) < 1e-6 for k in (0, 1) for e in (xz[:, k].min(), xz[:, k].max()))
            if gap < 0.25 and not on_box:
                del keep[i]
                changed = True
                break
    return np.array(keep)


def measure(asset):
    pts = glb_points(f"{DEPOT}/{asset}/{asset}.glb")
    lo, hi = pts.min(axis=0), pts.max(axis=0)
    box_centre = np.array([(lo[0] + hi[0]) / 2, (lo[2] + hi[2]) / 2])
    return hull(pts[:, [0, 2]]), box_centre, float(hi[1] - lo[1]), hi - lo


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", default="")
    args = ap.parse_args()
    measured = {asset: measure(asset) for asset in sorted({p[1] for p in LAYOUT})}

    structures, bindings, shapes = [], {}, []
    for sid, asset, x, z, material, extra in LAYOUT:
        footprint, box_centre, height, size = measured[asset]
        world = footprint - box_centre + np.array([x, z])
        drift = world.mean(axis=0) - np.array([x, z])   # where Fitting would centre the model, against where it belongs
        hull = ", ".join(f"[{c[0]:.2f}, {c[1]:.2f}]" for c in world)
        if asset in ROUND:
            radius = float(max(size[0], size[2])) / 2
            structures.append(f"- id: {sid}\n  circle_m: [{x:.2f}, {z:.2f}, {radius:.2f}]\n  height_m: {math.floor(height * 10) / 10:.1f}")
            angles = np.linspace(0, 2 * math.pi, 24, endpoint=False)
            world = np.c_[x + radius * np.cos(angles), z + radius * np.sin(angles)]
            drift = np.zeros(2)
        else:
            structures.append(f"- id: {sid}\n  hull_m: [{hull}]\n  height_m: {math.floor(height * 10) / 10:.1f}")
        entry = {"model": asset, "material": material}
        if np.hypot(*drift) > 0.01:
            entry["offset"] = [round(-float(drift[0]), 3), 0, round(-float(drift[1]), 3)]
        entry.update({k: v for k, v in extra.items() if k != "light_world"})
        if "light_world" in extra:
            # A light given in world metres, put into the model's own frame (its origin sits box_centre from the box's centre).
            wx, wy, wz = extra["light_world"]
            entry["light"] = {"at": [round(wx - (x - box_centre[0]), 2), wy, round(wz - (z - box_centre[1]), 2)], **extra["light"]}
        bindings[sid] = entry
        shapes.append((sid, world))

    # Region: structures between the markers (inserted before doors: on the first run), then the switches and the panel.
    text = open(REGION, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in text else "\n"
    block = nl.join([BEGIN] + [s.replace("\n", nl) for s in structures] + [END]) + nl
    if BEGIN in text:
        a, b = text.index(BEGIN), text.index(END) + len(END) + len(nl)
        text = text[:a] + block + text[b:]
    else:
        text = text.replace(nl + "doors:" + nl, nl + block + "doors:" + nl, 1)
    switch_lines = []
    for key, structure, flag, name, verb, requires, done in SWITCHES:
        s = [f"- key: {key}", f"  structure: {structure}", f"  flag_ref: {flag}"]
        if requires:
            s.append(f"  requires: [{', '.join(requires)}]")
        s += [f"  name: {name}", f"  verb: {verb}"]
        if requires:
            s.append(f"  locked_text: \"{NEEDLE_LOCKED}\"")
        s.append(f"  done_text: \"{done}\"")
        switch_lines += s
    sw_block = nl.join([BEGIN] + switch_lines + [END]) + nl
    head = text.index(nl + "switches:" + nl) + len(nl + "switches:" + nl)
    rest = text[head:]
    if rest.startswith(BEGIN):
        rest = rest[rest.index(END) + len(END) + len(nl):]
    text = text[:head] + sw_block + rest
    key, lore, page, name, verb = PANEL
    px, pz = next((x, z) for sid, _, x, z, _, _ in LAYOUT if sid == "fs_panel")
    rd_block = nl.join([f"  {BEGIN}", f"  - key: {key}", f"    lore_ref: {lore}", f"    page: {page}", f"    name: {name}",
                        f"    verb: {verb}", f"    circle_m: [{px:.1f}, {pz + 3.2:.1f}, 0.7]", "    height_m: 1.4", f"  {END}"]) + nl
    head = text.index(nl + "readables:" + nl) + len(nl + "readables:" + nl)
    rest = text[head:]
    if rest.startswith(f"  {BEGIN}"):
        rest = rest[rest.index(f"  {END}") + len(f"  {END}") + len(nl):]
    text = text[:head] + rd_block + rest
    open(REGION, "w", encoding="utf-8", newline="").write(text)

    # Bindings: the fs_* entries sit, one line each, at the head of "structures"; nothing else in the file is touched.
    lines = open(BINDINGS, encoding="utf-8").read().split("\n")
    lines = [l for l in lines if not l.startswith(('    "fs_', '    "creature.construct.gate_'))]
    head = lines.index('  "structures": {') + 1
    lines[head:head] = [f'    "{sid}": {json.dumps(entry)},' for sid, entry in bindings.items()]
    head = lines.index('  "creatures": {') + 1
    lines[head:head] = [f'    "{cid}": {json.dumps(entry)},' for cid, entry in CREATURES.items()]
    open(BINDINGS, "w", encoding="utf-8", newline="\n").write("\n".join(lines))
    json.load(open(BINDINGS, encoding="utf-8"))

    for sid, world in shapes:
        print(f"{sid:22s} {np.round(world.min(axis=0), 1)} .. {np.round(world.max(axis=0), 1)}  h {bindings[sid].get('model')}")
    if args.preview:
        preview(shapes, args.preview)


def preview(shapes, path):
    import yaml
    from PIL import Image, ImageDraw
    d = yaml.safe_load(open(REGION, encoding="utf-8"))
    h = np.array(d["terrain"]["heights_m"], dtype=float)[60:200, 340:560]   # z 660..800, x 740..960
    S = 6
    n = np.clip((h - 2) / 14, 0, 1)
    img = Image.fromarray(np.stack([70 + 150 * n, 90 + 140 * n, 60 + 120 * n], -1).astype(np.uint8)).resize((220 * S, 140 * S))
    dr = ImageDraw.Draw(img)
    P = lambda x, z: ((x - 740) * S, (z - 660) * S)
    for s in d["structures"]:
        c = s.get("circle_m")
        if c and 740 < c[0] < 960 and c[1] > 660:
            x, y = P(c[0], c[1])
            dr.ellipse((x - 3, y - 3, x + 3, y + 3), fill=(40, 70, 30))
    for sid, world in shapes:
        dr.polygon([P(*c) for c in world], outline=(240, 40, 240), fill=(90, 60, 110))
        cx, cz = world.mean(axis=0)
        dr.text(P(cx, cz), sid[3:], fill="white")
    for sp in ("tenders_west", "tenders_north", "tenders_east", "breach_west", "breach_northeast", "gate_warden"):
        s = yaml.safe_load(open(f"content/spawns/field/{sp}.yaml", encoding="utf-8"))
        x, y = P(*s["at"]["position_m"])
        dr.ellipse((x - 6, y - 6, x + 6, y + 6), fill=(230, 60, 0))
        for a, b in zip(s.get("route_m", []), (s.get("route_m", [])[1:] + s.get("route_m", [])[:1])):
            dr.line([P(*a), P(*b)], fill=(230, 120, 0), width=2)
    dr.ellipse((*P(850 - 24, 758 - 24), *P(850 + 24, 758 + 24)), outline=(255, 255, 0))
    dr.ellipse((*P(850 - 17, 758 - 17), *P(850 + 17, 758 + 17)), outline=(200, 200, 0))
    img.save(path)


if __name__ == "__main__":
    sys.exit(main())
