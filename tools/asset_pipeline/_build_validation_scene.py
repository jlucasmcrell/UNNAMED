"""Stage the playable asset set into the Godot project and write the validation gallery manifest.

`asset_gallery.gd` reads `res://assets/validation_scene.json`. This produces that manifest and the
staged files behind it, from the live library rather than from a hand-written list, so the gallery
cannot drift away from what actually exists.

Staging layout:
    assets/models/<asset_id>.glb        base meshes, rigged where a rig exists
    assets/clips/<clip_id>.glb          animation-only clips, beside the bodies they drive
    assets/materials/<material_id>/     PBR maps and the StandardMaterial3D resources
    assets/validation_scene.json        what the gallery draws and how it is arranged

Rows follow the sprint's own checklist, so opening the scene shows exactly the acceptance target
and a human can judge whether the sentence in section 29 is true yet.

Usage:
    python _build_validation_scene.py --apply
    python _build_validation_scene.py --audit
"""
import argparse
import io
import json
import os
import shutil
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
GODOT_PROJECT = os.environ.get("UNNAMED_GODOT_PROJECT", r"W:\UNNAMED\tools\godot_validate")
GODOT = os.environ.get("UNNAMED_GODOT",
                       r"W:\UNNAMED\tools\godot\Godot_v4.7.2-stable_console.exe")
STAGE = os.path.join(GODOT_PROJECT, "assets")
MODELS = os.path.join(STAGE, "models")
CLIPS = os.path.join(STAGE, "clips")
MANIFEST = os.path.join(STAGE, "validation_scene.json")

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

# Enemy clips, named by the short id the clip registry uses.
ENEMY_CLIPS = {
    "creature_frost_wolf": "frost_wolf",
    "creature_highland_brown_bear": "highland_brown_bear",
    "creature_bone_walker_husk": "bone_walker_husk",
    "creature_animated_armour": "animated_armour",
    "creature_great_river_serpent": "great_river_serpent",
}

WEAPONS = ["weapon_arming_sword", "weapon_yew_longbow_warbow", "weapon_boar_spear_hunting"]
PROPS = ["prop_iron_banded_oak_door", "container_chest_iron_banded",
         "prop_blacksmith_anvil_stump", "prop_carpenters_workbench",
         "container_barrel_oak", "travel_signpost_crossroads"]
KIT = ["building_wall_timber", "building_door_frame", "building_window_frame",
       "building_roof_panel", "building_floor_planks", "building_well",
       "building_fence_panel", "building_ruin_wall"]
FOLIAGE = ["flora_oak_tree", "flora_pine_tree", "flora_dead_tree", "flora_bramble_bush",
           "flora_fern_clump", "flora_heather_patch"]
ROCKS = ["rock_field_cluster", "rock_boulder", "rock_outcrop_shelf"]


def stage_glb(asset_id, prefer_rigged=True):
    """Copy one asset's base GLB into the project. Returns the res:// path or None."""
    directory = os.path.join(ASSETS, "ready", asset_id)
    source = None
    if prefer_rigged:
        rigged = os.path.join(ASSETS, "rigged", asset_id, f"{asset_id}_rigged.glb")
        if os.path.exists(rigged):
            source = rigged
    if source is None:
        candidate = os.path.join(directory, f"{asset_id}.glb")
        if os.path.exists(candidate):
            source = candidate
    if source is None:
        return None
    # The staged name carries the suffix so a rigged body and a base body never collide.
    suffix = "_rigged" if source.endswith("_rigged.glb") else ""
    target = os.path.join(MODELS, f"{asset_id}{suffix}.glb")
    os.makedirs(MODELS, exist_ok=True)
    shutil.copy2(source, target)
    return f"res://assets/models/{asset_id}{suffix}.glb"


def stage_clip(clip_id):
    for family in ("creatures", "humanoid", "mechanical"):
        source = os.path.join(ASSETS, "animation", "ready", family, f"{clip_id}.glb")
        if os.path.exists(source):
            os.makedirs(CLIPS, exist_ok=True)
            shutil.copy2(source, os.path.join(CLIPS, f"{clip_id}.glb"))
            return True
    return False


def entry(asset_id, prefer_rigged=True, **extra):
    path = stage_glb(asset_id, prefer_rigged)
    if path is None:
        return None
    item = {"name": asset_id, "path": path}
    item.update(extra)
    return item


def build_rows():
    rows = []

    player = []
    veth = entry("race2_veth_bindpose", clip="anim.humanoid.locomotion.idle",
                 socket_marker=[0.0, 0.78, 0.0], socket_name="SOCK_hand_R", spacing=2.0)
    if veth:
        player.append(veth)
    rows.append({
        "label": "Player - canonical Veth, 1.80 m",
        "scale_reference": True,
        "assets": player,
    })

    characters = []
    for asset_id, short in ENEMY_CLIPS.items():
        item = entry(asset_id, clip=f"anim.creature.{short}.idle", show_collision=True,
                     spacing=3.0)
        if item:
            characters.append(item)
    rows.append({"label": "Enemies - five, each playing idle", "assets": characters})

    weapons = []
    for asset_id in WEAPONS:
        item = entry(asset_id, prefer_rigged=False, spacing=2.5)
        if item:
            weapons.append(item)
    rows.append({"label": "Weapons - sword, bow, polearm", "assets": weapons})

    kit = []
    for asset_id in KIT:
        item = entry(asset_id, prefer_rigged=False, spacing=4.0)
        if item:
            kit.append(item)
    rows.append({"label": "Building kit - one 3.00 m module each", "assets": kit})

    props = []
    for asset_id in PROPS:
        item = entry(asset_id, prefer_rigged=False, spacing=3.0)
        if item:
            props.append(item)
    rows.append({"label": "Interaction props", "assets": props})

    foliage = []
    for asset_id in FOLIAGE + ROCKS:
        item = entry(asset_id, prefer_rigged=False, spacing=6.0)
        if item:
            foliage.append(item)
    rows.append({"label": "Foliage and rocks - trees are 14 m", "assets": foliage})

    return rows


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--no-import", action="store_true")
    args = parser.parse_args()

    rows = build_rows()
    staged = sum(len(r["assets"]) for r in rows)
    staged_clips = 0

    # Clips named by the rows, staged beside the bodies they drive.
    wanted = set()
    for row in rows:
        for asset in row["assets"]:
            if "clip" in asset:
                wanted.add(asset["clip"])
    for clip_id in sorted(wanted):
        if stage_clip(clip_id):
            staged_clips += 1

    summary = {
        "rows": len(rows),
        "assets": staged,
        "clips_staged": staged_clips,
        "clips_wanted": len(wanted),
    }

    print(f"  {'row':<44} {'assets':>7}")
    print("  " + "-" * 54)
    for row in rows:
        print(f"  {row['label']:<44} {len(row['assets']):>7}")
    print(f"\n  {staged} assets across {len(rows)} rows, {staged_clips}/{len(wanted)} clips staged")

    if not args.apply:
        print("  (audit only; pass --apply to write the manifest and stage the files)")
        return 0

    os.makedirs(STAGE, exist_ok=True)
    document = {
        "version": 1,
        "comment": [
            "What the asset validation gallery draws. Generated by _build_validation_scene.py from",
            "the live library, so the gallery cannot drift from what exists.",
            "",
            "This is an ASSET validation scene, not gameplay. It exists so a human can see whether",
            "the playable set is actually usable in the engine: right size, right way up, holding",
            "together as a set, animating, with sockets and collision where they were authored.",
        ],
        "rows": rows,
    }
    with io.open(MANIFEST, "w", encoding="utf-8") as handle:
        json.dump(document, handle, indent=2)
    print(f"  wrote {MANIFEST}")

    if not args.no_import and os.path.exists(GODOT):
        subprocess.run([GODOT, "--headless", "--path", GODOT_PROJECT, "--import"],
                       capture_output=True, text=True, timeout=900)
        print("  refreshed the Godot import cache")
    return 0


if __name__ == "__main__":
    sys.exit(main())
