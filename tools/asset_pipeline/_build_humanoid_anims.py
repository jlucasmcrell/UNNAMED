"""Build the player's general, combat and magic clip set: generated, registered, exported, verified.

Sprint section 6 lists the clips the player needs beyond the locomotion five that already exist:
interact, pickup, hit reaction, downed/death, sword ready/attack/block, polearm ready, bow
ready/draw-release, and one casting gesture for the M3e proof. This builds them through the chain
that is already proven for locomotion - generate the motion, register the clip metadata, retarget
onto the canonical skeleton, export an animation-only GLB - and reports the two numbers that
matter, baked duration and animated bone count.

Motion is generated rather than sourced, which section 6 permits explicitly: it asks for "reliable
gameplay-capable motion through the complete Blender to GLB to Godot pipeline", not final
animation. A real motion source replaces `_make_motion.py`'s tables, not this file.

Event times are declared as fractions of the clip and multiplied by the duration here, so a clip
whose length changes does not silently leave its attack commit behind at an old absolute time.

Usage:
    python _build_humanoid_anims.py --audit
    python _build_humanoid_anims.py --apply
    python _build_humanoid_anims.py --apply --clip anim.humanoid.magic.cast_01
"""
import argparse
import importlib.util
import io
import json
import os
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
SOURCE = os.path.join(ASSETS, "animation", "source", "manual")
CLIPS = os.path.join(ASSETS, "animation", "clips")
READY = os.path.join(ASSETS, "animation", "ready", "humanoid")
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
MOTION_TOOL = os.path.join(TOOL_DIR, "_make_motion.py")
RETARGET_TOOL = os.path.join(TOOL_DIR, "_blender_retarget.py")

# Durations are read from the generator rather than repeated here, so a clip's declared duration
# and the motion actually generated for it cannot drift apart.
_motion_spec = importlib.util.spec_from_file_location("_make_motion", MOTION_TOOL)
motion = importlib.util.module_from_spec(_motion_spec)
_motion_spec.loader.exec_module(motion)

REGISTRY_TOOL = os.path.join(TOOL_DIR, "_animation_registry.py")
_registry_spec = importlib.util.spec_from_file_location("_animation_registry", REGISTRY_TOOL)
registry = importlib.util.module_from_spec(_registry_spec)
_registry_spec.loader.exec_module(registry)

SKELETON_FAMILY = "humanoid_standard"
FPS = 30

# clip_id -> (motion kind, category, animation_family, clip_type, loop, events [(id, fraction)],
#             hand_profile, tags)
#
# `idle` is a clip TYPE, not a category: an idle belongs to locomotion alongside walk and run, and
# putting it in the category field is rejected by the registry validator.
CLIP_SPECS = {
    "anim.humanoid.general.interact_01": (
        "interact", "interaction", "general", "interact", False,
        [("interaction_point", 0.58)],
        {"left": "none", "right": "open"}, ["reach", "station"]),
    "anim.humanoid.general.pickup_01": (
        "pickup", "interaction", "general", "interact", False,
        [("interaction_point", 0.56)],
        {"left": "none", "right": "open"}, ["crouch", "reach", "ground"]),
    "anim.humanoid.general.hit_react_01": (
        "hit_react", "combat", "general", "react", False,
        [("hit_window_start", 0.0)],
        {"left": "none", "right": "none"}, ["flinch", "unarmed"]),
    "anim.humanoid.general.death_01": (
        "death", "death", "general", "death", False,
        [],
        {"left": "none", "right": "none"}, ["collapse", "unarmed", "terminal"]),
    "anim.humanoid.locomotion.turn_in_place_01": (
        "turn_in_place", "locomotion", "locomotion", "transition", False,
        [("foot_contact", 0.25), ("foot_contact", 0.60)],
        {"left": "none", "right": "none"}, ["turn", "in_place", "pose_only"]),
    "anim.humanoid.sword.ready_01": (
        "sword_ready", "combat", "sword", "state", True,
        [],
        {"left": "support", "right": "grip_primary"}, ["stance", "looping", "one_hand"]),
    "anim.humanoid.sword.attack_01": (
        "sword_attack", "combat", "sword", "attack", False,
        [("windup_start", 0.0), ("attack_commit", 0.24), ("hit_window_start", 0.34),
         ("hit_window_end", 0.46), ("recovery_start", 0.62)],
        {"left": "support", "right": "grip_primary"}, ["slash", "diagonal", "one_hand"]),
    "anim.humanoid.sword.block_01": (
        "sword_block", "combat", "sword", "defend", False,
        [("block_active_start", 0.15), ("block_active_end", 0.55)],
        {"left": "support", "right": "grip_primary"}, ["guard", "parry", "one_hand"]),
    "anim.humanoid.polearm.ready_01": (
        "polearm_ready", "combat", "polearm", "state", True,
        [],
        {"left": "grip_secondary", "right": "grip_primary"},
        ["stance", "looping", "two_hand"]),
    "anim.humanoid.bow.ready_01": (
        "bow_ready", "combat", "bow", "state", True,
        [],
        {"left": "grip_primary", "right": "open"}, ["stance", "looping", "ranged"]),
    "anim.humanoid.bow.draw_release_01": (
        "bow_draw_release", "combat", "bow", "attack", False,
        [("windup_start", 0.0), ("attack_commit", 0.30), ("projectile_release", 0.62),
         ("ammo_consumed", 0.66), ("recovery_start", 0.70)],
        {"left": "grip_primary", "right": "grip_secondary"}, ["draw", "release", "ranged"]),
    "anim.humanoid.magic.cast_01": (
        "cast", "magic", "magic", "cast", False,
        [("windup_start", 0.0), ("spell_release", 0.55), ("effect_spawn", 0.58),
         ("recovery_start", 0.72)],
        {"left": "support", "right": "open"}, ["casting", "gesture", "unarmed"]),
}


def duration_of(kind):
    return motion.ACTIONS[kind][0]


def build_one(clip_id, spec, apply_changes):
    kind, category, family, clip_type, loop, event_fractions, hand, tags = spec
    duration = duration_of(kind)

    motion_path = os.path.join(SOURCE, f"{kind}.json")
    generation = subprocess.run(
        [sys.executable, MOTION_TOOL, "--kind", kind, "--out", motion_path],
        capture_output=True, text=True, timeout=120)
    if not os.path.exists(motion_path):
        return None, f"motion generation failed: {(generation.stderr or '')[-160:]}"

    clip = {
        "schema_version": "1",
        "animation_id": clip_id,
        "skeleton_family": SKELETON_FAMILY,
        "category": category,
        "animation_family": family,
        "clip_type": clip_type,
        "loop": loop,
        "root_motion": False,
        "duration_s": duration,
        "events": [{"id": eid, "time": round(fraction * duration, 3)}
                   for eid, fraction in event_fractions],
        "hand_profile": hand,
        "tags": tags,
    }
    clip_path = os.path.join(CLIPS, f"{clip_id}.json")

    # The registry contract is checkable without Blender, so an audit catches a bad id, an
    # out-of-range event or an unknown hand profile in a second instead of after a bake.
    problems = registry.validate_clip(clip, clip_path)
    if problems:
        return None, "; ".join(problems)
    if not apply_changes:
        return clip, (f"{duration:.2f} s declared, {len(clip['events'])} events, "
                      f"loop={loop}, registry valid")

    os.makedirs(CLIPS, exist_ok=True)
    os.makedirs(READY, exist_ok=True)
    with io.open(clip_path, "w", encoding="utf-8") as handle:
        json.dump(clip, handle, indent=2)
        handle.write("\n")

    out_path = os.path.join(READY, f"{clip_id}.glb")
    animation = subprocess.run(
        [BLENDER, "--background", "--factory-startup", "--python", RETARGET_TOOL, "--",
         "--clip", clip_path, "--motion", motion_path, "--out", out_path, "--fps", str(FPS)],
        capture_output=True, text=True, timeout=900)

    report = None
    for line in (animation.stdout or "").splitlines():
        if line.startswith("RETARGET_RESULT "):
            report = json.loads(line[len("RETARGET_RESULT "):])
            break
    if report is None:
        tail = (animation.stdout or "")[-200:] + (animation.stderr or "")[-200:]
        return None, f"retarget failed: {tail}"
    if report["problems"]:
        return report, "; ".join(report["problems"])
    if not os.path.exists(out_path):
        return report, "no GLB written"

    mismatch = abs(report["duration_actual_s"] - duration)
    if mismatch > 0.02:
        return report, f"baked {report['duration_actual_s']} s against declared {duration} s"
    return report, (f"{report['duration_actual_s']:.2f} s, {report['bone_count']} bones, "
                    f"{len(clip['events'])} events")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--clip", nargs="*", default=None)
    args = parser.parse_args()

    targets = sorted(CLIP_SPECS)
    if args.clip:
        targets = [c for c in targets if c in args.clip]

    ok = failed = 0
    for clip_id in targets:
        report, detail = build_one(clip_id, CLIP_SPECS[clip_id], args.apply)
        bad = report is None or "failed" in detail or "against declared" in detail
        print(f"  {'FAIL' if bad else 'OK  '} {clip_id:<44} {detail}")
        if bad:
            failed += 1
        else:
            ok += 1

    print()
    print(f"  {ok} clips built, {failed} failed")
    if not args.apply:
        print("  (audit only; motion is generated but nothing is registered or exported)")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
