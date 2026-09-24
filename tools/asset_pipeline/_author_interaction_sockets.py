"""Author interaction anchors on world props, so gameplay can find where to reach.

`ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md` requires that "the character aligns to a known
anchor, then plays an interaction family", and names the vocabulary: `SOCK_interact_handle`,
`SOCK_interact_chest_lid`, `SOCK_interact_workbench`, `SOCK_interact_anvil`, `SOCK_interact_mount`.
No prop in the library carries any of them, which is why nothing is usable as an interactive object
yet even though every prop already has correct scale, LODs and collision.

Positions are derived from each mesh's measured bounds rather than typed in by hand, so an anchor
sits on the object it belongs to: a latch is on the front face at the measured half depth, a work
surface is at the measured height, and so on. A hand-written coordinate would silently drift the
moment a mesh is rescaled, and this library has just been rescaled.

Each socket carries a full orthonormal basis, matching the modular standard: a point plus one axis
cannot express roll, so an anchor without a basis leaves the character's facing ambiguous.

Usage:
    python _author_interaction_sockets.py --audit
    python _author_interaction_sockets.py --apply --asset container_chest_iron_banded
    python _author_interaction_sockets.py --apply --group interaction
"""
import argparse
import io
import json
import os
import struct
import sys

ASSETS = r"W:\UNNAMED\assets"
READY = os.path.join(ASSETS, "ready")
SOCKETS = os.path.join(ASSETS, "sockets")
SPEC = os.path.join(SOCKETS, "interaction_sockets.json")
JSON_CHUNK = 0x4E4F534A
COMPONENT = {5126: ("f", 4)}
TYPE_N = {"VEC3": 3}


def mesh_bounds(path):
    with open(path, "rb") as handle:
        data = handle.read()
    offset, gltf = 12, None
    while offset < len(data):
        length, kind = struct.unpack_from("<II", data, offset)
        offset += 8
        if kind == JSON_CHUNK:
            gltf = json.loads(data[offset:offset + length].decode("utf-8"))
        offset += length
    lo = [1e9] * 3
    hi = [-1e9] * 3
    for mesh in gltf.get("meshes", []):
        for prim in mesh.get("primitives", []):
            index = prim.get("attributes", {}).get("POSITION")
            if index is None:
                continue
            acc = gltf["accessors"][index]
            if "min" in acc:
                for k in range(3):
                    lo[k] = min(lo[k], acc["min"][k])
                    hi[k] = max(hi[k], acc["max"][k])
    return lo, hi


# Per role: how to place the anchor inside the measured bounds, and which way it faces.
# `at` is a fraction of the bounding box along each export-frame axis (X right, Y up, Z forward),
# so 1.0 is the maximum face and 0.5 is the centre. `primary` is the mating axis: for an
# interaction anchor it points OUT of the object, toward the character approaching it.
ROLES = {
    "SOCK_interact_handle": {
        "at": [0.78, 0.52, 1.0], "primary": [0, 0, 1], "secondary": [0, 1, 0],
        "role": "interact", "note": "Grasp point a character reaches for.",
    },
    "SOCK_interact_chest_lid": {
        "at": [0.5, 1.0, 0.5], "primary": [0, 1, 0], "secondary": [0, 0, -1],
        "role": "interact", "note": "Lid pivot line; the lid rotates about local X here.",
    },
    "SOCK_interact_workbench": {
        "at": [0.5, 1.0, 0.5], "primary": [0, 1, 0], "secondary": [0, 0, -1],
        "role": "interact", "note": "Working surface centre.",
    },
    "SOCK_interact_anvil": {
        "at": [0.5, 1.0, 0.5], "primary": [0, 1, 0], "secondary": [0, 0, -1],
        "role": "interact", "note": "Striking face centre.",
    },
    "SOCK_interact_take": {
        "at": [0.5, 0.85, 0.5], "primary": [0, 1, 0], "secondary": [0, 0, -1],
        "role": "interact", "note": "Where a pickup or node is grasped from.",
    },
    "SOCK_harvest_point": {
        "at": [0.5, 0.75, 1.0], "primary": [0, 0, 1], "secondary": [0, 1, 0],
        "role": "harvest", "note": "Where the gathering tool strikes the node.",
    },
    "SOCK_hinge": {
        "at": [0.0, 0.5, 0.5], "primary": [0, 1, 0], "secondary": [0, 0, -1],
        "role": "pivot", "note": "Door hinge axis; the leaf rotates about this vertical line.",
    },
}

# Which anchors each interactive prop needs. `group` drives bulk application.
INTERACTIVE = {
    "prop_iron_banded_oak_door": ("door", ["SOCK_hinge", "SOCK_interact_handle"]),
    "container_chest_iron_banded": ("container", ["SOCK_interact_chest_lid", "SOCK_interact_handle"]),
    "prop_banded_ash_sea_chest": ("container", ["SOCK_interact_chest_lid", "SOCK_interact_handle"]),
    "prop_blacksmith_anvil_stump": ("station", ["SOCK_interact_anvil"]),
    "prop_carpenters_workbench": ("station", ["SOCK_interact_workbench"]),
    "container_barrel_oak": ("container", ["SOCK_interact_handle", "SOCK_interact_take"]),
    "prop_wooden_crate": ("container", ["SOCK_interact_take"]),
    "prop_wooden_barrel": ("container", ["SOCK_interact_handle", "SOCK_interact_take"]),
    "prop_iron_banded_ore_cart": ("station", ["SOCK_interact_take"]),
    "prop_stack_of_firewood": ("pickup", ["SOCK_interact_take"]),
    "item_raw_iron_ore": ("resource", ["SOCK_interact_take"]),
    "resource_gold_ore_nugget": ("resource", ["SOCK_interact_take", "SOCK_harvest_point"]),
    "resource_healing_herb_leaf": ("resource", ["SOCK_interact_take", "SOCK_harvest_point"]),
    "resource_iron_ore_chunk": ("resource", ["SOCK_interact_take", "SOCK_harvest_point"]),
    "travel_signpost_crossroads": ("world", ["SOCK_interact_handle"]),
    "container_backpack_traveller": ("pickup", ["SOCK_interact_take"]),
    "container_belt_pouch_leather": ("pickup", ["SOCK_interact_take"]),
    # The rest of the blacksmithing chain's interaction surface. The outrcop is the ore-bearing
    # rock the player mines in the world, so it carries the harvest point rather than a take point;
    # the bellows is worked rather than picked up.
    "rock_outcrop_shelf": ("resource", ["SOCK_harvest_point"]),
    "prop_heap_of_raw_ore_chunks": ("resource", ["SOCK_harvest_point", "SOCK_interact_take"]),
    "prop_forge_double_bellows": ("station", ["SOCK_interact_handle"]),
    "item_grey_iron_ingot": ("pickup", ["SOCK_interact_take"]),
    "item_charcoal_steel_ingot": ("pickup", ["SOCK_interact_take"]),
    # Every remaining item in the prototype's own content list that the player takes off the ground
    # or out of a container. A mesh with no take anchor cannot be picked up, which makes it scenery
    # no matter how good it looks. These are the ids PROTOTYPE.md section 4.2 names; see
    # assets/manifests/prototype_crafting_chain.json for which built asset stands for which.
    "item_health_potion": ("pickup", ["SOCK_interact_take"]),
    "prop_leather_waterskin_flask": ("pickup", ["SOCK_interact_take"]),
    "resource_raw_hide": ("pickup", ["SOCK_interact_take"]),
    "resource_rawhide_pelt_fold": ("pickup", ["SOCK_interact_take"]),
    "magic_grimoire_bound": ("pickup", ["SOCK_interact_take"]),
    "magic_amulet_copper": ("pickup", ["SOCK_interact_take"]),
    "item_steel_plate_helm": ("pickup", ["SOCK_interact_take"]),
}


def build_spec(asset_id, roles):
    path = os.path.join(READY, asset_id, f"{asset_id}.glb")
    if not os.path.exists(path):
        return None
    lo, hi = mesh_bounds(path)
    if lo[0] > 1e8:
        return None
    sockets = {}
    for name in roles:
        rule = ROLES[name]
        position = [round(lo[axis] + (hi[axis] - lo[axis]) * rule["at"][axis], 5)
                    for axis in range(3)]
        sockets[name] = {
            "position": position,
            "primary": rule["primary"],
            "secondary": rule["secondary"],
            "roll": 0.0,
            "depth": 0.02,
            "envelope": None,
            "family": "interaction",
            "role": rule["role"],
            "mate": "antipodal",
            "note": rule["note"],
        }
    return {
        "asset_id": asset_id,
        "modular_interface_version": "0.1",
        "category": "interaction",
        "bounds_m": {"min": [round(v, 5) for v in lo], "max": [round(v, 5) for v in hi]},
        "sockets": sockets,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--asset", nargs="*", default=None)
    parser.add_argument("--group", nargs="*", default=None)
    args = parser.parse_args()

    targets = []
    for asset_id, (group, roles) in INTERACTIVE.items():
        if args.asset and asset_id not in args.asset:
            continue
        if args.group and group not in args.group:
            continue
        targets.append((asset_id, group, roles))

    os.makedirs(SOCKETS, exist_ok=True)
    written = []
    missing = []
    for asset_id, group, roles in sorted(targets):
        spec = build_spec(asset_id, roles)
        if spec is None:
            missing.append(asset_id)
            continue
        out = os.path.join(SOCKETS, f"{asset_id}.json")
        if args.apply:
            with io.open(out, "w", encoding="utf-8") as handle:
                json.dump(spec, handle, indent=2)
        written.append((asset_id, group, len(roles), spec["bounds_m"]))

    print(f"  {'asset':<34} {'group':<10} {'anchors':>7}  bounds (m)")
    print("  " + "-" * 78)
    for asset_id, group, count, bounds in written:
        span = [round(bounds["max"][a] - bounds["min"][a], 3) for a in range(3)]
        print(f"  {asset_id:<34} {group:<10} {count:>7}  {span}")

    print()
    print(f"  {len(written)} props specified, {len(missing)} not built")
    for asset_id in missing:
        print(f"    NOT BUILT: {asset_id}")
    if args.apply:
        # One combined file so the whole set can be read without walking the directory.
        combined = {}
        for asset_id, _g, _c, _b in written:
            combined[asset_id] = json.load(io.open(os.path.join(SOCKETS, f"{asset_id}.json"),
                                                   encoding="utf-8"))
        with io.open(SPEC, "w", encoding="utf-8") as handle:
            json.dump({"version": 1, "props": combined}, handle, indent=2)
        print(f"  wrote {len(written)} socket files and {SPEC}")
    else:
        print("  (audit only; pass --apply to write)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
