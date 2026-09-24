"""The animation state machine, as data, plus a loader that stages the clips into the Godot project.

Section 6 requires "AnimationLibrary compatibility" as a proof and section 12 records that the
shared-library and state-machine wiring is outstanding: the clips import individually and attach to
a character, but nothing has ever assembled them into one library keyed by their stable ids, and no
state machine has existed to check the ids against.

The state machine is data rather than a hand-wired .tscn because the ids are a contract. A tree
edited in the Godot editor stores whatever names the importer produced, and this project has
already been bitten by exactly that: Godot names an imported animation after the importing node, so
`anim.humanoid.locomotion.walk_forward` arrives as
`RIG_anim_humanoid_locomotion_walk_forwardAction`. Gameplay must key off the stable id, so the
mapping is generated here from the registry and the engine is made to serve that, not the reverse.

Every clip named below is read out of `assets/animation/clips/`, so this cannot reference a clip
that does not exist, and the validator then checks the same names resolve inside Godot.

Usage:
    python _make_anim_state_machine.py
"""
import io
import json
import os
import shutil

ASSETS = r"W:\UNNAMED\assets"
CLIPS = os.path.join(ASSETS, "animation", "clips")
READY = os.path.join(ASSETS, "animation", "ready")
OUT = os.path.join(ASSETS, "manifests", "animation_state_machine.json")

GODOT_PROJECT = os.environ.get("UNNAMED_GODOT_PROJECT", r"W:\UNNAMED\tools\godot_validate")
STAGE_CLIPS = os.path.join(GODOT_PROJECT, "assets", "clips")
STAGE_MANIFEST = os.path.join(GODOT_PROJECT, "assets", "animation_state_machine.json")
STAGE_REGISTRY = os.path.join(GODOT_PROJECT, "assets", "animation_registry.json")

# The one blended state, and what drives it. The speed values are a DECLARED ASSUMPTION: PROTOTYPE.md
# puts move speeds in `config/base_speeds` and never states them, so these are the locomotion
# reference points the blend is authored against rather than a reading of the design. What matters
# for the proof is that the four clips sit at four distinct speeds in one blend space; the numbers
# are gameplay-tunable and recorded here so tuning them is a data edit.
LOCOMOTION_SPEEDS = {
    "anim.humanoid.locomotion.idle": 0.0,
    "anim.humanoid.locomotion.walk_forward": 1.4,
    "anim.humanoid.locomotion.run_forward": 3.4,
    "anim.humanoid.locomotion.sprint_forward": 6.0,
}

# state name -> (kind, clip id, requires weapon, note)
#
#   blend_space_1d  driven continuously by the speed parameter
#   clip            a held state entered by request, left by request
#   one_shot        entered by request, leaves itself when the animation finishes
#   terminal        no automatic exit; gameplay decides what happens next
STATES = {
    "locomotion": ("blend_space_1d", None, None,
                   "The default state. Every other state returns here."),
    "sword_ready": ("clip", "anim.humanoid.sword.ready_01", "item.weapon.rusted_sword",
                    "Held guard with a one-handed sword."),
    "polearm_ready": ("clip", "anim.humanoid.polearm.ready_01", "weapon_boar_spear_hunting",
                      "Held two-handed polearm guard."),
    "bow_ready": ("clip", "anim.humanoid.bow.ready_01", "item.weapon.hunting_bow",
                  "Bow held, not yet drawn."),
    "sword_attack": ("one_shot", "anim.humanoid.sword.attack_01", "item.weapon.rusted_sword",
                     "Committed slash. Combat timing keys off attack_commit and the hit window."),
    "sword_block": ("one_shot", "anim.humanoid.sword.block_01", "item.weapon.rusted_sword",
                    "Parry. Block is active only between block_active_start and _end."),
    "polearm_thrust": ("one_shot", "anim.humanoid.polearm.thrust_01", "weapon_boar_spear_hunting",
                       "Two-handed thrust; needs both grip sockets for the runtime IK."),
    "bow_draw_release": ("one_shot", "anim.humanoid.bow.draw_release_01", "item.weapon.hunting_bow",
                         "Draw to full, release at projectile_release, arrow consumed after."),
    "cast": ("one_shot", "anim.humanoid.magic.cast_01", None,
             "Casting gesture. Unarmed: the prototype's item list has no staff."),
    "interact": ("one_shot", "anim.humanoid.general.interact_01", None,
                 "Reach and operate. Fired at interaction_point."),
    "pickup": ("one_shot", "anim.humanoid.general.pickup_01", None,
               "Crouch and take from the ground. Fired at interaction_point."),
    "hit_react": ("one_shot", "anim.humanoid.general.hit_react_01", None,
                  "Flinch. Interrupts anything except death."),
    "turn": ("one_shot", "anim.humanoid.locomotion.turn_in_place_01", None,
             "Pose-only turn step; gameplay owns the actual yaw."),
    "death": ("terminal", "anim.humanoid.general.death_01", None,
              "Terminal. Nothing transitions out of it automatically."),
}


def load_registry_clips():
    clips = {}
    for name in sorted(os.listdir(CLIPS)):
        if not name.endswith(".json"):
            continue
        with io.open(os.path.join(CLIPS, name), encoding="utf-8") as handle:
            data = json.load(handle)
        clips[data["animation_id"]] = data
    return clips


def main():
    clips = load_registry_clips()
    problems = []

    states = {}
    for name, (kind, clip_id, weapon, note) in STATES.items():
        entry = {"kind": kind, "note": note, "requires_weapon": weapon}
        if kind == "blend_space_1d":
            points = []
            for point_id, speed in LOCOMOTION_SPEEDS.items():
                if point_id not in clips:
                    problems.append(f"locomotion point '{point_id}' is not a registered clip")
                    continue
                points.append({"at_speed_mps": speed, "clip": point_id,
                               "loop": clips[point_id]["loop"]})
            entry["parameter"] = "speed_mps"
            entry["points"] = points
        else:
            if clip_id not in clips:
                problems.append(f"state '{name}' names clip '{clip_id}', which is not registered")
                continue
            entry["clip"] = clip_id
            entry["duration_s"] = clips[clip_id]["duration_s"]
            entry["loop"] = clips[clip_id]["loop"]
            entry["events"] = [event["id"] for event in clips[clip_id]["events"]]
            if kind == "one_shot":
                entry["return_to"] = "locomotion"
        states[name] = entry

    transitions = []
    for name, entry in states.items():
        if entry["kind"] == "one_shot":
            transitions.append({"from": "locomotion", "to": name,
                                "condition": "request == '%s'" % name})
            transitions.append({"from": name, "to": entry["return_to"],
                                "condition": "animation_finished"})
        elif entry["kind"] == "clip":
            transitions.append({"from": "locomotion", "to": name,
                                "condition": "request == '%s'" % name})
            transitions.append({"from": name, "to": "locomotion",
                                "condition": "request == ''"})
    # A hit reaction interrupts anything that is not already terminal.
    for name, entry in states.items():
        if name in ("hit_react", "death"):
            continue
        if entry["kind"] == "terminal":
            continue
        transitions.append({"from": name, "to": "hit_react",
                            "condition": "damage_taken and request == 'hit_react'"})
    transitions.append({"from": "*", "to": "death", "condition": "request == 'death'"})

    # Deduplicated, because two rules can legitimately want the same edge (a one-shot entered by
    # request and the same one-shot entered by an interrupt) and Godot rejects a duplicate edge.
    unique = []
    seen_edges = set()
    for transition in transitions:
        key = (transition["from"], transition["to"])
        if key in seen_edges:
            continue
        seen_edges.add(key)
        unique.append(transition)
    transitions = unique

    doc = {
        "version": 1,
        "comment": [
            "The player animation state machine, as data. Generated from the clip registry, so no",
            "state can name a clip that does not exist; validate_anim_tree.gd then checks the same",
            "names resolve inside Godot against a real AnimationLibrary.",
            "",
            "It is data rather than a .tscn because Godot renames imported animations after the",
            "importing node. Gameplay keys off these stable ids, so the engine is made to serve the",
            "ids rather than the ids being bent to whatever the importer produced.",
            "",
            "locomotion speeds are a declared assumption: PROTOTYPE.md puts move speeds in",
            "config/base_speeds and does not state them. They are tuning values, not design.",
        ],
        "skeleton_family": "humanoid_standard",
        "parameters": {
            "speed_mps": {"type": "float", "default": 0.0,
                          "note": "Ground speed, drives the locomotion blend space."},
            "request": {"type": "string", "default": "",
                        "note": "Names the state gameplay wants; empty returns to locomotion."},
            "damage_taken": {"type": "bool", "default": False},
        },
        "initial_state": "locomotion",
        "states": states,
        "transitions": transitions,
        "clip_count": len({s.get("clip") for s in states.values() if s.get("clip")}
                          | set(LOCOMOTION_SPEEDS)),
    }

    if problems:
        for problem in problems:
            print(f"  FAIL  {problem}")
        print(f"\n  {len(problems)} problem(s); manifest not written")
        return 1

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with io.open(OUT, "w", encoding="utf-8") as handle:
        json.dump(doc, handle, indent=2)
        handle.write("\n")

    # Stage everything Godot needs. The clips are copied rather than referenced because the Godot
    # project has to be self-contained to import them.
    #
    # Every clip in the registry is staged, not just this machine's seventeen, so the library the
    # validator builds covers the whole animation set - humanoid and creature - and every clip's
    # declared metadata is cross-checked against the engine in one place.
    os.makedirs(STAGE_CLIPS, exist_ok=True)
    staged = 0
    missing = []
    for clip_id in sorted(clips):
        source = None
        for group in ("humanoid", "creatures"):
            candidate = os.path.join(READY, group, f"{clip_id}.glb")
            if os.path.exists(candidate):
                source = candidate
                break
        if source is None:
            missing.append(clip_id)
            continue
        shutil.copy2(source, os.path.join(STAGE_CLIPS, f"{clip_id}.glb"))
        staged += 1
    if missing:
        for clip_id in missing:
            print(f"  FAIL  {clip_id}: registered but no exported GLB under ready/")
        return 1
    shutil.copy2(OUT, STAGE_MANIFEST)
    with io.open(STAGE_REGISTRY, "w", encoding="utf-8") as handle:
        json.dump({"clips": {k: clips[k] for k in sorted(clips)}}, handle, indent=2)

    print(f"  {'state':<16} {'kind':<16} {'clip':<46} duration")
    print("  " + "-" * 92)
    for name in sorted(states):
        entry = states[name]
        if entry["kind"] == "blend_space_1d":
            print(f"  {name:<16} {entry['kind']:<16} {len(entry['points'])} points in one blend "
                  f"space")
            continue
        print(f"  {name:<16} {entry['kind']:<16} {entry['clip']:<46} {entry['duration_s']}s")
    print()
    print(f"  {len(states)} states, {len(transitions)} transitions, {doc['clip_count']} clips")
    print(f"  wrote {OUT}")
    print(f"  staged {staged} clips into {STAGE_CLIPS}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
