"""Build the small shared NPC social and work animation proof set.

The maintenance brief authorises a **small shared** NPC animation set, on two conditions: it must
target the documented stable simplified NPC skeleton, and it must not create future armour lock-in.
Both are satisfied here by construction rather than by promise.

**Why one shared set rather than per-NPC clips.** All four Phase-1 NPCs carry a bone-for-bone
identical 20-bone skeleton - verified, not assumed:

    npc_veth_magistrate    20 bones
    npc_kal_smith          20 bones   identical to veth
    npc_siann_archivist    20 bones   identical to veth
    npc_orenth_guide       20 bones   identical to veth

So a clip binds to any of them, and four NPCs times five behaviours is twenty clips where five will do.
The set is built once against the standard-height NPC and applied by name.

**Why this cannot lock the NPCs out of armour later.** The clips animate only bones that the
simplified rig already owns, and they reference no canonical bone, no armour attachment and no
equipment socket. They are also recorded as joint angles rather than as baked bone-name bindings, so if
the NPC skeleton question is settled the other way the same authored poses can be re-cut against the
canonical rig without re-authoring the motion. Nothing here migrates Tavar or the player onto an
equippable rig, and no armour work is started.

Clips are animation-only: no mesh, no skin weights, the body supplies geometry and the clip supplies
motion. The Godot validator treats that as a supported case (`is_animation_only`).

Usage:
    python _build_npc_anims.py --audit
    python _build_npc_anims.py --apply
    python _build_npc_anims.py --apply --only talk
"""
import argparse
import io
import json
import os
import subprocess
import sys

ASSETS = r"W:\UNNAMED\assets"
RIGGED = os.path.join(ASSETS, "rigged")
SOURCE = os.path.join(ASSETS, "animation", "source", "npc")
CLIPS = os.path.join(ASSETS, "animation", "clips")
READY = os.path.join(ASSETS, "animation", "ready", "npc")
BLENDER = os.environ.get("UNNAMED_BLENDER",
                         r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe")
TOOL_DIR = os.path.dirname(os.path.abspath(__file__))
MOTION_TOOL = os.path.join(TOOL_DIR, "_make_creature_motion.py")
ANIM_TOOL = os.path.join(TOOL_DIR, "_blender_anim_creature.py")

# The source rig. The standard-height NPC, so amplitudes are authored at 1.80 m rather than at the
# smith's 1.30 m; the motion generator scales by the asset's size hint.
SOURCE_RIG = "npc_veth_magistrate"
RIG_PLAN = "humanoid"
SKELETON_FAMILY = "creature_humanoid"
ANIMATION_FAMILY = "npc"
FPS = 30

# kind -> (clip_type, category, loop, root_motion, events)
#
# Categories and clip types are drawn from the registry's declared sets: `social` and `profession` are
# real categories, and `emote`, `interact` and `idle` are real clip types. `talk` is an emote rather
# than an idle because it is a deliberate gesture; `sit` is an idle because it is a state the NPC
# holds.
CLIP_SPECS = {
    "talk":       ("emote",    "social",      True,  False, []),
    "handover":   ("interact", "interaction", False, False, [("interaction_point", 0.42)]),
    "work_forge": ("emote",    "profession",  True,  False,
                   [("strike_contact", 0.25), ("strike_contact", 0.75)]),
    "work_table": ("emote",    "profession",  True,  False, []),
    "sit":        ("idle",     "social",      True,  False, []),
}

# The seed height for the seated clip. Sitting is level design as much as animation, so the assumption
# is declared rather than left implicit: the clip lowers the hips by 0.45 * the rig's height hint.
SEAT_HEIGHT_NOTE = ("hips lowered by 0.45 x the source rig's size hint; a seat at roughly 0.45 m "
                    "above the floor for a 1.80 m NPC")


def build_one(kind, apply_changes):
    clip_type, category, loop, root_motion, events = CLIP_SPECS[kind]
    clip_id = f"anim.npc.{kind}"

    os.makedirs(SOURCE, exist_ok=True)
    motion_path = os.path.join(SOURCE, f"npc_{kind}.json")
    generation = subprocess.run(
        [sys.executable, MOTION_TOOL, "--plan", RIG_PLAN, "--kind", kind,
         "--asset-id", SOURCE_RIG, "--out", motion_path],
        capture_output=True, text=True, timeout=180)
    if not os.path.exists(motion_path):
        return None, f"motion generation failed: {(generation.stderr or '')[-160:]}"

    os.makedirs(READY, exist_ok=True)
    out_path = os.path.join(READY, f"{clip_id}.glb")
    animation = subprocess.run(
        [BLENDER, "--background", "--factory-startup", "--python", ANIM_TOOL, "--",
         "--rigged", os.path.join(RIGGED, SOURCE_RIG, f"{SOURCE_RIG}_rigged.glb"),
         "--motion", motion_path, "--out", out_path, "--fps", str(FPS)],
        capture_output=True, text=True, timeout=900)

    report = None
    for line in (animation.stdout or "").splitlines():
        if line.startswith("CREATURE_ANIM_RESULT "):
            report = json.loads(line[len("CREATURE_ANIM_RESULT "):])
            break
    if report is None:
        return None, f"animation failed: {(animation.stdout or '')[-180:]}"
    if report.get("bones_missing"):
        return report, f"bones in motion but not in the rig: {report['bones_missing']}"
    if not os.path.exists(out_path):
        return report, "no GLB written"
    if not apply_changes:
        return report, None

    duration = (report.get("frames", 0) - 1) / float(FPS) if report.get("frames") else None
    clip = {
        "schema_version": "1",
        "animation_id": clip_id,
        "skeleton_family": SKELETON_FAMILY,
        "category": category,
        "animation_family": ANIMATION_FAMILY,
        "clip_type": clip_type,
        "loop": loop,
        "root_motion": root_motion,
        "duration_s": duration,
        "events": [{"id": event_id, "time": time} for event_id, time in events],
        "source": {
            "rig": SOURCE_RIG,
            "rig_plan": RIG_PLAN,
            "tool": "_make_creature_motion.py",
            "shared": True,
        },
        "applies_to": ["npc_veth_magistrate", "npc_kal_smith", "npc_siann_archivist",
                       "npc_orenth_guide"],
        "reference_pose": "A-pose",
        "notes": (
            "Shared NPC social/work clip. Binds to any of the four Phase-1 NPCs because they carry "
            "bone-for-bone identical 20-bone skeletons. Animates only bones the simplified rig owns "
            "and references no canonical bone, armour attachment or equipment socket, so it cannot "
            "cause future armour lock-in."
            + (f" Seated: {SEAT_HEIGHT_NOTE}." if kind == "sit" else "")),
    }
    os.makedirs(CLIPS, exist_ok=True)
    with io.open(os.path.join(CLIPS, f"{clip_id}.json"), "w", encoding="utf-8") as handle:
        json.dump(clip, handle, indent=2)
        handle.write("\n")
    return report, None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--audit", action="store_true")
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--only", default=None)
    args = parser.parse_args()

    kinds = [args.only] if args.only else sorted(CLIP_SPECS)
    for kind in kinds:
        if kind not in CLIP_SPECS:
            print(f"  unknown kind '{kind}'; known: {sorted(CLIP_SPECS)}")
            return 1
        report, problem = build_one(kind, args.apply)
        if report is None:
            print(f"  FAIL {kind}: {problem}")
            continue
        status = "ok" if not problem else f"PROBLEM: {problem}"
        print(f"  {kind:<12} frames {report.get('frames'):>4}  "
              f"bones keyed {len(report.get('bones_keyed') or []):>3}  {status}")

    print()
    print(f"  {'applied' if args.apply else 'audit only'} — {len(kinds)} clip(s) in the proof set")
    if args.apply:
        print(f"  GLBs   : {READY}")
        print(f"  metadata: {CLIPS}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
