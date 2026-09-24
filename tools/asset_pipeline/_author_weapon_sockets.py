"""Author grip, head and attachment sockets on the three prototype weapons.

The prototype needs exactly three weapon families, and a weapon that cannot be held is not a
weapon. Each socket's position is taken from the CENTROID of the vertices in its band along the
weapon's length, not from the bounding box, because a box centre on a curved bow or a
blade-heavy spear lands beside the shaft rather than on it.

Placements are derived from measured geometry (`_weapon_profile.py`), and each one is verified to
sit on the mesh before it is written: a socket floating in space looks fine in a node list and
puts the hand in mid-air. The band fractions below come from those profiles, and are stated here
rather than inferred at run time so the choice is auditable.

Facing follows the modular standard's grip rule: a grip socket's `primary` points along the grip
axis TOWARD THE HEAD. The arming sword came out of the generator point-down, so its grip faces
-Y while the spear's faces +Y.

Usage:
    python _author_weapon_sockets.py --audit
    python _author_weapon_sockets.py --apply
"""
import argparse
import io
import json
import math
import os
import struct
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
SOCKETS = os.path.join(ASSETS, "sockets")
SPEC = os.path.join(SOCKETS, "weapon_sockets.json")
JSON_CHUNK = 0x4E4F534A

# family, [(socket, band centre as a fraction of length, primary axis, role, note)]
#
# Band fractions read off the measured profiles:
#   arming sword  blade 0.03-0.66, crossguard spike 0.72, grip and pommel above
#   yew longbow   widest at 0.46 (the riser and grip), limbs tapering either side
#   boar spear    shaft 0.04-0.68, head socket 0.75, blade 0.82-0.96
WEAPONS = {
    "weapon_arming_sword": [
        ("SOCK_grip_primary", 0.80, [0, -1, 0], "grip",
         "Main hand, just above the crossguard. Primary faces the blade, which is downward."),
        ("SOCK_pommel", 0.97, [0, 1, 0], "pommel", "Butt of the hilt."),
        ("SOCK_attach", 0.45, [0, -1, 0], "mount",
         "Balance point; where a scabbard or back strap holds it when stowed."),
    ],
    "weapon_yew_longbow_warbow": [
        ("SOCK_grip_primary", 0.46, [0, 1, 0], "grip",
         "Riser grip at the measured mid-point. Primary runs up the bow toward the upper limb."),
        ("SOCK_ammo", 0.46, [0, 0, -1], "ammo",
         "Nocking point: where the arrow crosses the string, offset toward the archer."),
        ("SOCK_attach", 0.46, [0, 0, 1], "mount", "Where the bow rides when slung."),
    ],
    "weapon_boar_spear_hunting": [
        ("SOCK_grip_primary", 0.32, [0, 1, 0], "grip",
         "Rear hand on the shaft. Primary faces the head, which is up."),
        ("SOCK_grip_secondary", 0.50, [0, 1, 0], "grip",
         "Support hand. Required for a two-handed weapon to drive runtime IK."),
        ("SOCK_head", 0.86, [0, 1, 0], "head", "The spear head, where a replacement would seat."),
        ("SOCK_pommel", 0.03, [0, -1, 0], "pommel", "Butt of the shaft."),
        ("SOCK_attach", 0.32, [0, 0, 1], "mount", "Where it rides on the back when stowed."),
    ],
    # A held tool uses the same grip contract as a weapon: whatever a character holds needs a
    # socket its hand mates to. The mining pick is the last missing link in the blacksmithing
    # chain, and without a grip it cannot be swung.
    "tool_mining_pick": [
        ("SOCK_grip_primary", 0.40, [0, 1, 0], "grip",
         "Mid-haft. Primary faces the head, which the measured profile puts at the top."),
        ("SOCK_head", 0.90, [0, 1, 0], "head", "The pick head."),
        ("SOCK_pommel", 0.03, [0, -1, 0], "pommel", "Butt of the haft."),
        ("SOCK_attach", 0.40, [0, 0, 1], "mount", "Where it rides on the belt or pack."),
    ],
    # The other two held tools in the blacksmithing chain. Both are gripped by a character, so
    # both need the same contract as the pick; without a grip they cannot be used at all.
    "tool_blacksmith_hammer": [
        ("SOCK_grip_primary", 0.35, [0, 1, 0], "grip",
         "Mid-haft. The profile puts the head at 0.81-0.94, so primary faces up."),
        ("SOCK_head", 0.90, [0, 1, 0], "head", "The hammer head."),
        ("SOCK_pommel", 0.03, [0, -1, 0], "pommel", "Butt of the haft."),
    ],
    "tool_blacksmith_tongs": [
        ("SOCK_grip_primary", 0.40, [0, -1, 0], "grip",
         "Mid-handle; the two arms measure about 0.22 m across. Primary faces the jaws."),
        ("SOCK_head", 0.08, [0, -1, 0], "head", "The jaws, at the measured narrow end."),
    ],
    # The prototype's actual starter weapon. PROTOTYPE.md step 1 equips the rusted sword before the
    # player has done anything, so of the 112 weapons in the library this is the one that most
    # needs a grip, and it had none. Profile at 40 bins: tip 0.00-0.07, blade 0.07-0.67, crossguard
    # 0.69-0.71 (0.256 m wide), grip 0.74-0.91 (about 0.05 m across), pommel 0.94-0.99.
    "weapon_rusted_militia_sword": [
        ("SOCK_grip_primary", 0.82, [0, -1, 0], "grip",
         "Mid-grip, between the crossguard at 0.70 and the pommel at 0.94. Like the arming sword "
         "this one exports point-down, so primary faces -Y, toward the blade."),
        ("SOCK_pommel", 0.97, [0, 1, 0], "pommel", "Butt of the hilt."),
        ("SOCK_attach", 0.45, [0, -1, 0], "mount",
         "Balance point on the blade; where a scabbard or back strap holds it when stowed."),
    ],
}

BAND_HALF_WIDTH = 0.02      # fraction of length either side of the band centre
MIN_NEARBY_VERTICES = 12    # a socket with no geometry near it is not on the weapon


def load_positions(path):
    with open(path, "rb") as handle:
        data = handle.read()
    offset, gltf, binary = 12, None, None
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        payload = data[offset:offset + length]
        if kind == JSON_CHUNK:
            gltf = json.loads(payload.decode("utf-8"))
        else:
            binary = payload
        offset += length
    out = []
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            acc = gltf["accessors"][prim["attributes"]["POSITION"]]
            view = gltf["bufferViews"][acc["bufferView"]]
            start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
            stride = view.get("byteStride") or 12
            for i in range(acc["count"]):
                out.append(struct.unpack_from("<fff", binary, start + i * stride))
    return out


def band_centroid(verts, fraction, half_width):
    """Centroid and grip envelope of the vertices in one band along the length axis (export Y).

    The envelope uses a high PERCENTILE of the radial distance rather than the maximum. The bow's
    riser carries a cross-brace and a sight that stick out a third of a metre, so the maximum
    reported a 0.64 m "grip" - three times too wide for a hand. A percentile describes the part a
    hand actually closes around and ignores whatever hardware protrudes from it.
    """
    ys = [v[1] for v in verts]
    low, high = min(ys), max(ys)
    span = high - low
    centre_y = low + span * fraction
    half = span * half_width
    slab = [v for v in verts if abs(v[1] - centre_y) <= half]
    if not slab:
        return None
    cx = sum(v[0] for v in slab) / len(slab)
    cz = sum(v[2] for v in slab) / len(slab)
    distances = sorted(math.hypot(v[0] - cx, v[2] - cz) for v in slab)
    envelope_radius = distances[min(int(len(distances) * 0.60), len(distances) - 1)]
    return {"position": [round(cx, 5), round(centre_y, 5), round(cz, 5)],
            "vertices": len(slab),
            "radius": round(envelope_radius, 4),
            "max_radius": round(distances[-1], 4)}


def build_spec(asset_id):
    path = os.path.join(READY, asset_id, f"{asset_id}.glb")
    if not os.path.exists(path):
        return None, "not built"
    verts = load_positions(path)
    if not verts:
        return None, "no geometry"

    sockets = {}
    problems = []
    for name, fraction, primary, role, note in WEAPONS[asset_id]:
        band = band_centroid(verts, fraction, BAND_HALF_WIDTH)
        if band is None:
            problems.append(f"{name}: no geometry at fraction {fraction}")
            continue
        if band["vertices"] < MIN_NEARBY_VERTICES:
            problems.append(f"{name}: only {band['vertices']} vertices near fraction {fraction}, "
                            "so the socket would float off the weapon")
            continue
        # Envelope is the measured cross-section: a hand has to be able to close around it.
        sockets[name] = {
            "position": band["position"],
            "primary": primary,
            "secondary": [0, 0, 1] if abs(primary[1]) == 1 else [0, 1, 0],
            "roll": 0.0,
            "depth": 0.02,
            "envelope": round(max(band["radius"] * 2.0, 0.02), 4),
            "family": "standard",
            "role": role,
            "mate": "antipodal",
            "note": note,
            "measured_radius_m": band["radius"],
            "band_vertices": band["vertices"],
        }

    if problems:
        return None, "; ".join(problems)
    return {
        "asset_id": asset_id,
        "modular_interface_version": "0.1",
        "category": "weapon",
        "fit_family": "standard_humanoid",
        "length_m": round(max(v[1] for v in verts) - min(v[1] for v in verts), 4),
        "sockets": sockets,
    }, None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    args = parser.parse_args()

    os.makedirs(SOCKETS, exist_ok=True)
    written = {}
    print(f"  {'weapon':<32} {'socket':<24} {'pos (export m)':<28} r")
    print("  " + "-" * 92)
    for asset_id in sorted(WEAPONS):
        spec, problem = build_spec(asset_id)
        if spec is None:
            print(f"  FAIL {asset_id}: {problem}")
            continue
        written[asset_id] = spec
        for name, socket in spec["sockets"].items():
            print(f"  {'OK  '} {asset_id:<27} {name:<24} "
                  f"{str(socket['position']):<28} {socket['measured_radius_m']}")

    if args.apply and written:
        for asset_id, spec in written.items():
            with io.open(os.path.join(SOCKETS, f"{asset_id}.json"), "w", encoding="utf-8") as h:
                json.dump(spec, h, indent=2)
        with io.open(SPEC, "w", encoding="utf-8") as handle:
            json.dump({"version": 1, "weapons": written}, handle, indent=2)
        print(f"\n  wrote {len(written)} weapon socket specs and {SPEC}")
    elif not args.apply:
        print("\n  (audit only; pass --apply to write)")

    missing = [a for a in WEAPONS if a not in written]
    if missing:
        print(f"  incomplete: {missing}")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
