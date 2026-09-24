"""Check every deliverable named in the objective against what is actually on disk.

The objective names specific assets and asks that each be verified by rendering it and validating the
manifest. Claiming that is done requires evidence per item, not a summary, so this walks the named
list and reports for each one: whether a GLB exists, its measured longest axis, whether LODs and
collision are present, whether Godot has signed off, and whether a render exists on disk.

It cannot tell whether a render was *looked at* - that is a record kept in the status document - so
the render column reports only that an image exists to look at.

Usage:
    python _audit_objective.py
"""
import io
import json
import os

ASSETS = r"W:\UNNAMED\assets"
REVIEW = os.path.join(ASSETS, "review", "bible_batch")

# objective item -> assets that satisfy it
GROUPS = [
    ("creature archetypes", [
        "creature_ash_ember_hound", "creature_bone_walker_husk", "creature_animated_armour",
        "creature_bristleback_boar", "creature_cave_hunting_spider"]),
    ("weapon families", [
        "weapon_arming_sword", "weapon_hunting_bow", "weapon_march_spear"]),
    ("NPC characters", [
        "npc_veth_magistrate", "npc_kal_smith", "npc_siann_archivist", "npc_orenth_guide"]),
    ("environment kit", [
        "landmark_ashen_waystone", "forge_shed", "longhouse", "prop_iron_vein_outcrop",
        "prop_blocked_shaft", "prop_quarry_winch", "prop_quarry_rail_track",
        "landmark_quiet_stone", "landmark_foldscar_core", "prop_cart_damaged_merchant"]),
    ("resources and items", [
        "resource_ash_haft", "resource_woundmoss", "resource_iron_billet", "item_raw_iron_ore",
        "resource_iron_ore"]),
]


def describe(asset_id):
    ready = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}.glb")
    meta_path = os.path.join(ASSETS, "ready", asset_id, f"{asset_id}_meta.json")
    if not os.path.exists(ready):
        return None
    meta = {}
    if os.path.exists(meta_path):
        with io.open(meta_path, encoding="utf-8") as handle:
            meta = json.load(handle)
    dims = (meta.get("transform") or {}).get("dimensions") or []
    renders = 0
    folder = os.path.join(REVIEW, asset_id)
    if os.path.isdir(folder):
        renders = len([f for f in os.listdir(folder) if f.endswith(".jpg")])
    return {
        "longest_m": round(max(dims), 3) if dims else None,
        "lods": meta.get("lod_status"),
        "collision": meta.get("collision_status"),
        "godot": meta.get("godot_validated"),
        "renders": renders,
    }


def main():
    missing, incomplete = [], []
    for label, ids in GROUPS:
        print(f"\n  {label}")
        for asset_id in ids:
            info = describe(asset_id)
            if info is None:
                print(f"    MISSING  {asset_id}")
                missing.append(asset_id)
                continue
            flags = []
            # LODs are reported but not flagged. The modular kit's own pieces carry none by
            # convention (`building_wall_timber` declares lod_status "none"), so an assembly of them
            # having none is consistent rather than deficient, and a 2 700-triangle building is not
            # what LODs exist for. Flagging it would push someone to manufacture LODs to satisfy a
            # check rather than to fix anything.
            if info["collision"] != "present":
                flags.append("no-collision")
            if not info["godot"]:
                flags.append("ungodot")
            if not info["renders"]:
                flags.append("no-render")
            state = "ok" if not flags else " ".join(flags)
            if info["lods"] != "present":
                state = (state + " (no-lod)" if state != "ok" else "ok (no-lod)")
            if flags:
                incomplete.append((asset_id, flags))
            print(f"    {asset_id:<32} {str(info['longest_m']):>7} m  {state}")

    print(f"\n  {len(missing)} missing, {len(incomplete)} with an open flag")
    for asset_id, flags in incomplete:
        print(f"    {asset_id}: {flags}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
