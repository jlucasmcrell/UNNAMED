"""Build the five-enemy animation set: six clips each, registered and verified.

Sprint section 9 requires each enemy to carry idle, locomotion, attack, hit reaction and death.
Locomotion is split into walk and run, so that is six clips per enemy and thirty in total.

Every clip is generated from the creature's own rig plan, applied to its own armature, verified to
be animation-only, and registered with the clip metadata the animation document asks for but never
supplies: ids, durations, loop flags, root-motion flags and event times.

Usage:
    python _build_creature_anims.py --audit
    python _build_creature_anims.py --apply
    python _build_creature_anims.py --apply --enemy creature_frost_wolf
"""
import argparse
import io
import json
import os
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
RIGGED = os.path.join(ASSETS, "rigged")
SOURCE = os.path.join(ASSETS, "animation", "source", "creatures")
CLIPS = os.path.join(ASSETS, "animation", "clips")
READY = os.path.join(ASSETS, "animation", "ready", "creatures")
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
MOTION_TOOL = os.path.join(TOOL_DIR, "_make_creature_motion.py")
ANIM_TOOL = os.path.join(TOOL_DIR, "_blender_anim_creature.py")

# Enemies, with the rig plan and the registry's skeleton family for each. The families differ
# because the rigs do: a hound is not a person and a serpent is not either.
#
# PHASE 1 ROSTER. These five are the archetypes `PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md`
# section 10 names, and section 35 requires all five to exist as distinct data definitions rather
# than one creature relabelled. Three of them - the hound, boar and spider - had no clips at all
# until they were added here; the animated armour and bone walker husk were already covered.
#
# The spider is rigged to the quadruped plan because that is the only legged plan the rig tool has.
# Eight legs therefore cannot articulate independently and the walk reads as a four-limbed gait.
# That is a real limitation of the rig, not of the motion, and it needs an arthropod rig plan to fix.
ENEMIES = {
    "creature_ash_ember_hound": ("quadruped", "creature_quadruped", "ash_ember_hound"),
    "creature_bone_walker_husk": ("humanoid", "creature_humanoid", "bone_walker_husk"),
    "creature_animated_armour": ("humanoid", "creature_humanoid", "animated_armour"),
    "creature_bristleback_boar": ("quadruped", "creature_quadruped", "bristleback_boar"),
    "creature_cave_hunting_spider": ("quadruped", "creature_quadruped", "cave_hunting_spider"),

    # Superseded roster, kept so their clips stay reproducible. Their six clips each are already on
    # disk and are not rebuilt by a plain --apply, which walks the Phase 1 roster above. They are
    # not part of the Ashen Hollow content target.
    "creature_frost_wolf": ("quadruped", "creature_quadruped", "frost_wolf"),
    "creature_highland_brown_bear": ("quadruped", "creature_quadruped", "highland_brown_bear"),
    "creature_great_river_serpent": ("worm", "creature_serpentine", "great_river_serpent"),
}

# A plain --apply builds only these, so adding the superseded three back to the dict above cannot
# silently re-render work that is already finished.
PHASE1_ROSTER = [
    "creature_ash_ember_hound",
    "creature_bone_walker_husk",
    "creature_animated_armour",
    "creature_bristleback_boar",
    "creature_cave_hunting_spider",
]

# kind -> (clip_type, category, loop, root_motion, events)
#
# `idle` is a CLIP TYPE, not a category: the registry's categories are broad families
# (locomotion, combat, interaction, death, ...) and an idle belongs to locomotion alongside walk
# and run. Putting "idle" in the category field was rejected by the registry validator.
CLIP_DEFS = {
    "idle":   ("idle",   "locomotion", True,  False, []),
    "walk":   ("move",   "locomotion", True,  False,
               [("foot_contact", 0.0), ("foot_leave", 0.5)]),
    "run":    ("move",   "locomotion", True,  False,
               [("foot_contact", 0.0), ("foot_leave", 0.5)]),
    "attack": ("attack", "combat",     False, False,
               [("windup_start", 0.0), ("attack_commit", 0.25),
                ("hit_window_start", 0.28), ("hit_window_end", 0.45),
                ("recovery_start", 0.62)]),
    "hit":    ("react",  "combat",     False, False, [("hit_window_start", 0.0)]),
    "death":  ("death",  "death",      False, False, []),
}

DURATIONS = {"idle": 4.0, "walk": 1.2, "run": 0.8, "attack": 0.9, "hit": 0.55, "death": 1.9}


def build_one(enemy_id, plan, skeleton_family, short_name, kind, apply_changes):
    motion_path = os.path.join(SOURCE, f"{short_name}_{kind}.json")
    generation = subprocess.run(
        [sys.executable, MOTION_TOOL, "--plan", plan, "--kind", kind,
         "--asset-id", enemy_id, "--out", motion_path],
        capture_output=True, text=True, timeout=120)
    if not os.path.exists(motion_path):
        return None, f"motion generation failed: {(generation.stderr or '')[-140:]}"

    clip_id = f"anim.creature.{short_name}.{kind}"
    out_path = os.path.join(READY, f"{clip_id}.glb")
    animation = subprocess.run(
        [BLENDER, "--background", "--factory-startup", "--python", ANIM_TOOL, "--",
         "--rigged", os.path.join(RIGGED, enemy_id, f"{enemy_id}_rigged.glb"),
         "--motion", motion_path, "--out", out_path, "--fps", "30"],
        capture_output=True, text=True, timeout=600)

    report = None
    for line in (animation.stdout or "").splitlines():
        if line.startswith("CREATURE_ANIM_RESULT "):
            report = json.loads(line[len("CREATURE_ANIM_RESULT "):])
            break
    if report is None:
        return None, f"animation failed: {(animation.stdout or '')[-160:]}"
    if report["bones_missing"]:
        return report, f"bones in motion but not in the rig: {report['bones_missing']}"
    if not os.path.exists(out_path):
        return report, "no GLB written"

    clip_type, category, loop, root_motion, events = CLIP_DEFS[kind]
    clip = {
        "schema_version": "1",
        "animation_id": clip_id,
        "skeleton_family": skeleton_family,
        "category": category,
        "animation_family": "creature",
        "clip_type": clip_type,
        "loop": loop,
        "root_motion": root_motion,
        "duration_s": DURATIONS[kind],
        "events": [{"id": e, "time": t} for e, t in events],
        "hand_profile": {"left": "none", "right": "none"},
        "tags": ["creature", plan] + (["looping"] if loop else []),
    }
    if apply_changes:
        os.makedirs(CLIPS, exist_ok=True)
        os.makedirs(READY, exist_ok=True)
        with io.open(os.path.join(CLIPS, f"{clip_id}.json"), "w", encoding="utf-8") as handle:
            json.dump(clip, handle, indent=2)

    mismatch = abs(report["duration_s"] - DURATIONS[kind])
    if mismatch > 0.02:
        return report, f"baked {report['duration_s']} s against declared {DURATIONS[kind]} s"
    return report, f"{report['duration_s']} s, {len(report['bones_keyed'])} bones"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--enemy", nargs="*", default=None)
    parser.add_argument("--all", action="store_true",
                        help="Include the superseded roster, not just the Phase 1 five")
    args = parser.parse_args()

    enemies = sorted(ENEMIES) if args.all else sorted(PHASE1_ROSTER)
    if args.enemy:
        unknown = [e for e in args.enemy if e not in ENEMIES]
        if unknown:
            print(f"  unknown enemy id(s): {unknown}")
            return 1
        enemies = [e for e in args.enemy]

    ok = failed = 0
    for enemy_id in enemies:
        plan, family, short = ENEMIES[enemy_id]
        print(f"  {enemy_id}  ({plan}, {family})")
        for kind in ("idle", "walk", "run", "attack", "hit", "death"):
            report, detail = build_one(enemy_id, plan, family, short, kind, args.apply)
            good = report is not None and "bones in motion" not in detail and "against declared" not in detail
            print(f"    {'OK  ' if good else 'FAIL'} {kind:<7} {detail}")
            if good:
                ok += 1
            else:
                failed += 1

    print(f"\n  {ok} clips built, {failed} failed")
    if not args.apply:
        print("  (audit only; pass --apply to register the clip metadata)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
