"""The authoritative animation registry: schema, stable IDs, and validation.

`ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md` section 19 sketches clip metadata and notes
"the exact schema is future work". This is that schema, and section 64's rule is the reason it
matters:

> Do not bind authoritative combat logic directly to arbitrary imported clip names.
> Use stable animation IDs/metadata.

So a clip's identity is an **id in a registry**, not a filename in a GLB. Combat logic refers
to `anim.humanoid.polearm.thrust_01` and its declared event times; the GLB that happens to
carry it can be replaced, re-exported or re-sourced without touching gameplay code.

Nothing here requires Blender or a GPU. It is the contract that the Blender side writes
against and the game reads.

Usage:
    python _animation_registry.py --validate          # check every clip file
    python _animation_registry.py --list              # summarize by family
    python _animation_registry.py --events <id>       # show one clip's timing
"""
import argparse
import json
import os
import re
import sys

ASSETS = os.environ.get("UNNAMED_ASSETS", r"W:\UNNAMED\assets")
CLIPS_DIR = os.path.join(ASSETS, "animation", "clips")
SCHEMA_VERSION = "1"

# Stable id grammar: anim.<skeleton-or-creature>.<family>.<name>
# Lower-case, dot-separated, name may carry a _NN suffix for numbered variants. The id is
# the contract, so the grammar is enforced rather than encouraged.
ID_PATTERN = re.compile(r"^anim\.[a-z0-9_]+(\.[a-z0-9_]+)+$")

SKELETON_FAMILIES = {
    "humanoid_standard", "kal_compact_winged", "vaskaal_tall_articulated",
    "ondrek_heavy", "constructed_standard", "mor_special",
    "creature_quadruped", "creature_humanoid", "creature_serpentine",
    "creature_winged", "creature_arthropod", "mechanical",
}

CATEGORIES = {"locomotion", "combat", "interaction", "magic", "traversal",
              "social", "profession", "facial", "death", "mechanical"}

CLIP_TYPES = {"idle", "move", "transition", "attack", "defend", "react", "cast",
              "interact", "emote", "death", "deploy", "state"}

# Hand profiles, matching the socket roles in the modular asset standard.
HAND_PROFILES = {"grip_primary", "grip_secondary", "grip_flat", "grip_vaskaal",
                 "open", "point", "fist", "none", "support"}

# Event ids that combat and interaction systems may key on. A closed vocabulary, because an
# event nobody consumes is drift and an event nobody declares is a hard-coded frame number.
EVENT_IDS = {
    "windup_start", "attack_commit", "hit_window_start", "hit_window_end",
    "recovery_start", "projectile_release", "ammo_consumed", "block_active_start",
    "block_active_end", "foot_contact", "foot_leave", "deploy_lock_complete",
    "spell_release", "effect_spawn", "sound_cue", "interaction_point",
    "grip_release", "grip_acquire", "root_motion_start", "root_motion_end",
}


def clip_path(animation_id):
    return os.path.join(CLIPS_DIR, animation_id + ".json")


def validate_clip(data, path):
    """Return a list of problems; empty means valid."""
    problems = []
    where = os.path.basename(path)

    animation_id = data.get("animation_id")
    if not animation_id:
        problems.append(f"{where}: missing animation_id")
    else:
        if not ID_PATTERN.match(animation_id):
            problems.append(f"{where}: id '{animation_id}' does not match "
                            f"anim.<family>.<name> grammar")
        if os.path.basename(path) != animation_id + ".json":
            problems.append(f"{where}: filename does not match animation_id "
                            f"'{animation_id}'")

    if data.get("schema_version") != SCHEMA_VERSION:
        problems.append(f"{where}: schema_version must be '{SCHEMA_VERSION}'")

    family = data.get("skeleton_family")
    if family not in SKELETON_FAMILIES:
        problems.append(f"{where}: unknown skeleton_family '{family}'")

    if data.get("category") not in CATEGORIES:
        problems.append(f"{where}: unknown category '{data.get('category')}'")
    if data.get("clip_type") not in CLIP_TYPES:
        problems.append(f"{where}: unknown clip_type '{data.get('clip_type')}'")

    for flag in ("loop", "root_motion"):
        if not isinstance(data.get(flag), bool):
            problems.append(f"{where}: '{flag}' must be a boolean")

    duration = data.get("duration_s")
    if not isinstance(duration, (int, float)) or duration <= 0:
        problems.append(f"{where}: duration_s must be a positive number")

    # Events must be ordered, in range, and from the closed vocabulary.
    events = data.get("events", [])
    if not isinstance(events, list):
        problems.append(f"{where}: events must be a list")
        events = []
    last = -1.0
    for index, event in enumerate(events):
        if not isinstance(event, dict):
            problems.append(f"{where}: event {index} is not an object")
            continue
        event_id = event.get("id")
        if event_id not in EVENT_IDS:
            problems.append(f"{where}: event {index} id '{event_id}' is not in the "
                            f"vocabulary")
        time = event.get("time")
        if not isinstance(time, (int, float)):
            problems.append(f"{where}: event {index} time must be a number")
            continue
        if isinstance(duration, (int, float)) and not (0 <= time <= duration):
            problems.append(f"{where}: event '{event_id}' at {time}s is outside "
                            f"0..{duration}s")
        if time < last:
            problems.append(f"{where}: event '{event_id}' at {time}s is out of order")
        last = max(last, time)

    # A combat clip without a commit or hit window cannot drive combat timing.
    if data.get("category") == "combat":
        ids = {e.get("id") for e in events if isinstance(e, dict)}
        if data.get("clip_type") == "attack" and not ids & {"attack_commit",
                                                            "hit_window_start"}:
            problems.append(f"{where}: combat attack clip declares neither "
                            f"attack_commit nor hit_window_start")

    hand = data.get("hand_profile")
    if not isinstance(hand, dict):
        problems.append(f"{where}: hand_profile must be an object")
    else:
        for side in ("left", "right"):
            if hand.get(side) not in HAND_PROFILES:
                problems.append(f"{where}: hand_profile.{side} "
                                f"'{hand.get(side)}' is not a known profile")

    if not isinstance(data.get("tags"), list):
        problems.append(f"{where}: tags must be a list")

    return problems


def load_clips():
    if not os.path.isdir(CLIPS_DIR):
        return []
    clips = []
    for name in sorted(os.listdir(CLIPS_DIR)):
        if not name.endswith(".json"):
            continue
        path = os.path.join(CLIPS_DIR, name)
        try:
            with open(path, encoding="utf-8") as handle:
                clips.append((path, json.load(handle)))
        except (OSError, ValueError) as exc:
            clips.append((path, {"__load_error__": str(exc)}))
    return clips


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--validate", action="store_true")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--events", metavar="ANIMATION_ID")
    args = parser.parse_args()

    clips = load_clips()
    if not clips:
        print(f"  no clips in {CLIPS_DIR}")
        print("  the schema and validator are ready; clips appear once motion is sourced")
        return 0

    if args.events:
        for path, data in clips:
            if data.get("animation_id") == args.events:
                print(f"  {args.events}")
                print(f"    duration {data.get('duration_s')}s  "
                      f"loop={data.get('loop')}  root_motion={data.get('root_motion')}")
                for event in data.get("events", []):
                    print(f"    {event['time']:>6.2f}s  {event['id']}")
                return 0
        print(f"  no clip with id {args.events}")
        return 1

    if args.list:
        by_family = {}
        for _path, data in clips:
            by_family.setdefault(data.get("animation_family", "?"), []).append(
                data.get("animation_id"))
        for family in sorted(by_family):
            print(f"  {family} ({len(by_family[family])})")
            for animation_id in sorted(by_family[family]):
                print(f"    {animation_id}")
        return 0

    total_problems = 0
    valid = 0
    for path, data in clips:
        if "__load_error__" in data:
            print(f"  BAD  {os.path.basename(path)}: {data['__load_error__']}")
            total_problems += 1
            continue
        problems = validate_clip(data, path)
        if problems:
            total_problems += len(problems)
            print(f"  BAD  {data.get('animation_id', os.path.basename(path))}")
            for problem in problems:
                print(f"         {problem}")
        else:
            valid += 1
    print()
    print(f"  {valid}/{len(clips)} clips valid, {total_problems} problem(s)")
    return 1 if total_problems else 0


if __name__ == "__main__":
    sys.exit(main())
