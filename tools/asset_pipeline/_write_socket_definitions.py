"""Write the Wave 0 proof-set socket definitions.

These are the interface contract for the proof set, authored once so the whole set is
internally consistent rather than fifteen hand-written files that drift apart.

Coordinates are in the **export frame** (Y-up, base at ground, footprint centred), which is
what `_blender_sockets.py` expects and what an engine sees.

Convention reminder:
  primary    the mating axis; +primary points INTO the mating part
  secondary  fixes roll; must not be parallel to primary
  mate       antiparallel for a joint between two parts, aligned for a continuation
"""
import json
import os

OUT_DIR = r"W:\UNNAMED\assets\sockets"
VERSION = "0.1"


def socket(position, primary, secondary, depth=0.02, envelope=0.030,
           family="standard", role="", mate="antiparallel", roll=0.0, joint_type=None):
    """One modular interface.

    mate describes this socket's own orientation intent. joint_type optionally states
    the relationship of the PAIR, which is what actually decides legality - a per-socket
    flag cannot express a two-sided relationship on its own.
    """
    spec = {
        "position": list(position), "primary": list(primary), "secondary": list(secondary),
        "roll": roll, "depth": depth, "envelope": envelope,
        "family": family, "role": role, "mate": mate,
    }
    if joint_type:
        spec["joint_type"] = joint_type
    return spec


# Shafts run along export +Y. A haft has a head socket at the top and a pommel socket at
# the bottom, and its own longitudinal axis IS the grip axis, so the primary of the top
# socket points up into the head and the bottom socket points down into the pommel.
def haft(asset_id, length, grips, nominal):
    sockets = {
        "SOCK_head": socket((0.0, length, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                            depth=0.03, role="head", mate="antiparallel"),
        "SOCK_pommel": socket((0.0, 0.0, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0, 1.0),
                              depth=0.025, role="pommel", mate="antiparallel"),
    }
    for name, y, family in grips:
        sockets[name] = socket((0.0, y, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                               depth=0.0, envelope=0.030 if family == "standard" else 0.036,
                               family=family, role="grip", mate="aligned")
    return {
        "asset_id": asset_id, "modular_interface_version": VERSION,
        "nominal_size_m": nominal, "category": "weapon_component",
        "fit_family": None, "sockets": sockets,
    }


DEFINITIONS = [
    # 1 mace head: sockets downward onto a haft
    {
        "asset_id": "weaponcomp_mace_head_flanged_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.09, 0.115, 0.09],
        "category": "weapon_component", "fit_family": None,
        "sockets": {
            "SOCK_head": socket((0.0, 0.0, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0, 1.0),
                                depth=0.035, role="head", mate="antiparallel"),
        },
    },
    # 2 short haft: one grip
    haft("weaponcomp_haft_short_a", 0.42,
         [("SOCK_grip_primary", 0.10, "standard")], [0.034, 0.42, 0.034]),
    # 3 long haft: primary plus secondary grip, as the doc's two-handed case
    haft("weaponcomp_haft_long_a", 1.55,
         [("SOCK_grip_primary", 0.42, "standard"),
          ("SOCK_grip_secondary", 0.68, "standard")], [0.034, 1.55, 0.034]),
    # 4 pommel: sockets upward onto the haft butt
    {
        "asset_id": "weaponcomp_pommel_counterweight_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.045, 0.075, 0.045],
        "category": "weapon_component", "fit_family": None,
        "sockets": {
            "SOCK_pommel": socket((0.0, 0.075, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                  depth=0.025, role="pommel", mate="antiparallel"),
        },
    },
    # 5 arming blade: seats on a grip via its tang at the base
    {
        "asset_id": "weaponcomp_blade_arming_sword_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.048, 0.82, 0.012],
        "category": "weapon_component", "fit_family": None,
        "sockets": {
            # Opposes the grip socket. The blade tang enters the grip from above, so the
            # blade's +primary points DOWN while the grip's points UP into the tang.
            # Envelope is the LARGER of the two grip families it must accept, because a
            # shared component has to declare the largest interface it will mate with.
            "SOCK_grip_primary": socket((0.0, 0.0, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0, 1.0),
                                        depth=0.12, envelope=0.036,
                                        role="grip", mate="antiparallel"),
        },
    },
    # 6 heater shield: mounts to the forearm, so the mount socket faces the arm
    {
        "asset_id": "weaponcomp_shield_heater_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.45, 0.60, 0.045],
        "category": "weapon_component", "fit_family": "standard_humanoid",
        "sockets": {
            # Same outward basis as the body reference it mounts to, so 'aligned'.
            "SOCK_body_mount": socket((0.0, 0.30, 0.022), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0),
                                       depth=0.0, envelope=0.0,
                                       role="body_mount", mate="aligned"),
        },
    },
    # 7 chest plate: standard fit family, outward layer offset 0.024
    {
        "asset_id": "armour_chest_plate_base_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.34, 0.46, 0.16],
        "category": "armour", "fit_family": "standard_humanoid",
        "sockets": {
            "SOCK_layer_root": socket((0.0, 0.30, 0.0), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0),
                                      depth=0.0, envelope=0.0,
                                      role="layer_root", mate="aligned"),
            # Gap pieces mount outward from the chest at the neck, not onto the layer root.
            "SOCK_body_mount": socket((0.0, 0.44, 0.06), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0),
                                       depth=0.0, envelope=0.0,
                                       role="body_mount", mate="aligned"),
        },
    },
    # 8 gambeson underlayer: same family, inner layer offset 0.008
    {
        "asset_id": "armour_chest_underlayer_gambeson_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.36, 0.48, 0.17],
        "category": "armour", "fit_family": "standard_humanoid",
        "sockets": {
            "SOCK_layer_root": socket((0.0, 0.30, 0.0), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0),
                                      depth=0.0, envelope=0.0,
                                      role="layer_root", mate="aligned"),
        },
    },
    # 9 gorget: midline gap piece, no LOD chain by the small-component rule
    {
        "asset_id": "armour_gorget_plate_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.22, 0.14, 0.20],
        "category": "armour", "fit_family": "standard_humanoid",
        "sockets": {
            # Outward from the chest, matching SOCK_body_mount so the pair is 'aligned'.
            "SOCK_body_mount": socket((0.0, 0.0, 0.0), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0),
                                       depth=0.0, envelope=0.0,
                                       role="body_mount", mate="aligned"),
        },
    },
    # 10 standard grip
    {
        "asset_id": "weaponcomp_grip_standard_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.032, 0.115, 0.032],
        "category": "weapon_component", "fit_family": "standard_humanoid",
        "sockets": {
            # +primary points UP into the blade tang above it, opposing the blade's socket.
        "SOCK_grip_primary": socket((0.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                        depth=0.10, envelope=0.036,
                                        family="standard", role="grip", mate="antiparallel"),
        },
    },
    # 11 Vaskaal grip: same interface role, different family and envelope
    {
        "asset_id": "weaponcomp_grip_vaskaal_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.038, 0.135, 0.038],
        "category": "weapon_component", "fit_family": "tall_narrow",
        "sockets": {
            # Same mating axis as the standard grip, but a 36 mm family envelope. This is the
        # cross-family case: the blade drops onto either grip, and the envelope check is
        # what makes it safe rather than the family name.
        "SOCK_grip_primary": socket((0.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                        depth=0.12, envelope=0.036,
                                        family="vaskaal", role="grip", mate="antiparallel"),
        },
    },
    # 12 Kal back channel: compact_broad, wing channels either side of the spine
    {
        "asset_id": "armour_kal_back_channel_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.40, 0.46, 0.14],
        "category": "armour", "fit_family": "compact_broad",
        "sockets": {
            "SOCK_layer_root": socket((0.0, 0.30, 0.0), (0.0, 0.0, -1.0), (0.0, 1.0, 0.0),
                                      depth=0.0, envelope=0.0,
                                      role="layer_root", mate="aligned"),
            "SOCK_wing_channel_L": socket((0.06, 0.30, 0.0), (0.0, 0.0, -1.0), (0.0, 1.0, 0.0),
                                          depth=0.0, envelope=0.05,
                                          family="compact_broad", role="wing_channel",
                                          mate="aligned"),
            "SOCK_wing_channel_R": socket((-0.06, 0.30, 0.0), (0.0, 0.0, -1.0), (0.0, 1.0, 0.0),
                                          depth=0.0, envelope=0.05,
                                          family="compact_broad", role="wing_channel",
                                          mate="aligned"),
        },
    },
    # 13 focus crystal in its mount: screws into a staff, so channel socket at its base
    {
        "asset_id": "magiccomp_focus_crystal_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.075, 0.10, 0.075],
        "category": "magic_component", "fit_family": None,
        "sockets": {
            # +primary points DOWN into the staff channel below it, opposing the staff.
            "SOCK_channel_focus": socket((0.0, 0.0, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0, 1.0),
                                         depth=0.02, envelope=0.024,
                                         role="channel_focus", mate="antiparallel"),
        },
    },
    # 14 telescoping mechanism: both ends continue the shaft, so both are 'aligned'
    {
        "asset_id": "weaponcomp_mechanism_telescope_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.036, 0.20, 0.036],
        "category": "weapon_component", "fit_family": None,
        "sockets": {
            # Opposes the shaft's mechanism socket below it: +primary points UP into the
            # shaft's socket, so the two are antiparallel and the joint opposes.
            # Both ends of a continuation mechanism are antiparallel: one mates down into
            # the shaft, the other receives whatever stacks above it.
            "SOCK_mechanism_in": socket((0.0, 0.0, 0.0), (0.0, -1.0, 0.0), (0.0, 0.0, 1.0),
                                        depth=0.03, role="mechanism", mate="antiparallel",
                                        joint_type="antiparallel"),
            # Antiparallel to the staff head socket above it, so the joint opposes.
            # Opposes the head socket above it: the mechanism's +primary points UP into
            # the part that seats on top.
            "SOCK_mechanism_out": socket((0.0, 0.20, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                         depth=0.03, role="mechanism", mate="antiparallel",
                                         joint_type="antiparallel"),
            "SOCK_deploy": socket((0.0, 0.10, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                  depth=0.0, role="deploy", mate="aligned"),
        },
        "mechanism": {
            "family": "telescoping",
            "pivot_socket": "SOCK_deploy",
            "track": {"axis": [0.0, 1.0, 0.0], "min_m": 0.0, "max_m": 0.20,
                      "indexed": [0.0, 0.20]},
            "bone": "mech_telescope",
            "collision_states": ["stowed", "deployed"],
        },
    },
    # 14b sliding-rail mechanism: translation along a track, NOT axial extension. This is
    # the family that differs most from telescoping, so it needs its own proof asset.
    {
        "asset_id": "weaponcomp_mechanism_slide_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.05, 0.26, 0.05],
        "category": "weapon_component", "fit_family": None,
        "sockets": {
            "SOCK_mechanism": socket((0.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                      depth=0.03, role="mechanism", mate="antiparallel",
                                      joint_type="antiparallel"),
            "SOCK_slide_carrier": socket((0.0, 0.13, 0.0), (0.0, 0.0, 1.0), (0.0, 1.0, 0.0),
                                          depth=0.0, envelope=0.02,
                                          role="slide_carrier", mate="aligned"),
        },
        "mechanism": {
            "family": "sliding_rail",
            "pivot_socket": "SOCK_slide_carrier",
            "track": {"axis": [0.0, 1.0, 0.0], "min_m": 0.0, "max_m": 0.14,
                      "indexed": [0.0, 0.07, 0.14]},
            "bone": "mech_slide",
            "collision_states": ["stowed", "deployed"],
            "note": "the carrier translates along the rail without changing its own size",
        },
    },
    # 15 assembled hybrid: mode-dependent grip positions, which is what state_attachments
    # exists for. The grip moves down the shaft when the spear deploys.
    {
        "asset_id": "weapon_hybrid_focus_staff_spear_a",
        "modular_interface_version": VERSION,
        "nominal_size_m": [0.05, 1.90, 0.05],
        "category": "weapon", "fit_family": None,
        "sockets": {
            "SOCK_grip_primary": socket((0.0, 0.70, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                        depth=0.0, envelope=0.030,
                                        role="grip", mate="aligned"),
            # Opposes the focus crystal mounted above it: +primary points UP into it.
            "SOCK_channel_focus": socket((0.0, 1.60, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                         depth=0.02, envelope=0.024,
                                         role="channel_focus", mate="antiparallel"),
            # A mechanism socket lets an extension be inserted between shaft and head.
            # +primary points UP into the mechanism seated above it.
            "SOCK_mechanism": socket((0.0, 1.60, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                      depth=0.03, role="mechanism", mate="antiparallel",
                                      joint_type="antiparallel"),
            "SOCK_head": socket((0.0, 1.90, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0),
                                depth=0.03, role="head", mate="antiparallel"),
        },
        "mechanism": {
            "family": "telescoping",
            "pivot_socket": "SOCK_channel_focus",
            "track": {"axis": [0.0, 1.0, 0.0], "min_m": 0.0, "max_m": 0.30,
                      "indexed": [0.0, 0.30]},
            "bone": "mech_extend",
            "collision_states": ["stowed", "deployed"],
            "state_attachments": {
                "stowed": {"SOCK_grip_primary": [0.0, 0.70, 0.0]},
                "deployed": {"SOCK_grip_primary": [0.0, 0.95, 0.0]},
            },
        },
    },
]


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    written = 0
    for definition in DEFINITIONS:
        path = os.path.join(OUT_DIR, definition["asset_id"] + ".json")
        with open(path, "w", encoding="utf-8") as handle:
            json.dump(definition, handle, indent=2)
        written += 1
    print(f"  wrote {written} socket definitions to {OUT_DIR}")
    for definition in DEFINITIONS:
        names = sorted(definition["sockets"].keys())
        print(f"    {definition['asset_id']:<40} {len(names)} socket(s): {', '.join(names)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
