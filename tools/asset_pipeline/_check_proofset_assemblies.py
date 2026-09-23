"""Run every legal assembly the Wave 0 proof set must satisfy.

Exits non-zero if any combination is illegal, so this is the gate the proof set passes or
fails rather than a report someone has to read carefully.
"""
import importlib.util
import os
import sys

TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location(
    "assembly", os.path.join(TOOL_DIR, "_check_assembly.py"))
assembly = importlib.util.module_from_spec(spec)
spec.loader.exec_module(assembly)

# (label, asset_a, socket_a, asset_b, socket_b)
COMBINATIONS = [
    # Required assembly 1: one head on both haft lengths
    ("mace head on SHORT haft", "weaponcomp_haft_short_a", "SOCK_head",
     "weaponcomp_mace_head_flanged_a", "SOCK_head"),
    ("mace head on LONG haft", "weaponcomp_haft_long_a", "SOCK_head",
     "weaponcomp_mace_head_flanged_a", "SOCK_head"),
    # Required assembly 2: chest plate over gambeson, plus gorget
    ("gambeson under chest plate", "armour_chest_underlayer_gambeson_a", "SOCK_layer_root",
     "armour_chest_plate_base_a", "SOCK_layer_root"),
    ("gorget mounts to chest", "armour_gorget_plate_a", "SOCK_body_mount",
     "armour_chest_plate_base_a", "SOCK_body_mount"),
    # Required assembly 3: ONE shared component on BOTH grip families
    ("arming blade on STANDARD grip", "weaponcomp_blade_arming_sword_a", "SOCK_grip_primary",
     "weaponcomp_grip_standard_a", "SOCK_grip_primary"),
    ("arming blade on VASKAAL grip", "weaponcomp_blade_arming_sword_a", "SOCK_grip_primary",
     "weaponcomp_grip_vaskaal_a", "SOCK_grip_primary"),
    # Pommel chain: pommel onto haft butt
    ("pommel on short haft", "weaponcomp_haft_short_a", "SOCK_pommel",
     "weaponcomp_pommel_counterweight_a", "SOCK_pommel"),
    # Focus crystal into the staff channel
    ("focus crystal into staff", "weapon_hybrid_focus_staff_spear_a", "SOCK_channel_focus",
     "magiccomp_focus_crystal_a", "SOCK_channel_focus"),
    # Mechanism continues the shaft
    # Mechanism inserted between the shaft and the head - the stacking case.
    ("telescope inserts into hybrid", "weapon_hybrid_focus_staff_spear_a", "SOCK_mechanism",
     "weaponcomp_mechanism_telescope_a", "SOCK_mechanism_in"),
    # Shield mounts to a standard body
    ("shield mounts to body reference", "weaponcomp_shield_heater_a", "SOCK_body_mount",
     "armour_chest_plate_base_a", "SOCK_body_mount"),
    # Kal wing channels present on the back piece
    ("Kal back channel to wing L", "armour_kal_back_channel_a", "SOCK_wing_channel_L",
     "armour_kal_back_channel_a", "SOCK_wing_channel_L"),
]


def main():
    failures = 0
    for label, a, sa, b, sb in COMBINATIONS:
        try:
            result = assembly.check(assembly.load(a), sa, assembly.load(b), sb)
        except SystemExit as exc:
            print(f"  SKIP {label}: {exc}")
            continue
        ok = not result["problems"]
        if not ok:
            failures += 1
        print(f"  {'LEGAL  ' if ok else 'ILLEGAL'} {label}")
        print(f"            angle {result['angle_deg']:>6.3f} deg   "
              f"primary {result['primary_dot']:+.5f}   secondary {result['secondary_dot']:+.5f}")
        for note in result["notes"]:
            print(f"            note: {note}")
        for problem in result["problems"]:
            print(f"            {problem}")

    print()
    print(f"  {len(COMBINATIONS) - failures}/{len(COMBINATIONS)} combinations legal")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
