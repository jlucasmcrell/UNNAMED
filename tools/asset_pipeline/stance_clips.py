"""The standing poses the owner flagged (2026-10-17: wide-stance idle, clenched fists, a drawn sword held out like a T-pose), rebuilt
from motion capture on both player skeletons, plus the MetaHuman's combat set.

    python stance_clips.py --out D:\\Otherreach_Scratch\\anim\\staging [--only idle,sword_ready] [--rigs prod,mh,mh_combat]

Every clip keeps its id; player.cw_player.talk and player.mh.talk / sword_ready / the combat states are new ids on the MetaHuman
(bindings data only). Sources (sources table below; the Mixamo-rig takes are FBX turned into GLB by ue_export/anim_fbx_to_glb.py,
the Rokoko take cut with --frames):
  * idle: Fab Sample Animation Pack AS_M_Unarmed_Idle_01 - unarmed mocap idle, ankles 0.31 m over 0.20 m hips, arms hanging
    (13 deg off vertical), hands relaxed (about 70 deg of finger curl). It replaces UAL1 Idle_Loop (keyframed; ankles 0.63 m on
    the production rig, fists at 155 deg).
  * talk: the same pack's AS_M_Unarmed_Gesture_01 (one-handed explaining gesture), so a conversation no longer freezes the player.
  * sword_ready: Rokoko free mocap SwordIdleMedium, 3-11 s of the take (the window whose end pose meets its start): a one-handed
    sword held low at the side, arms 10-15 deg off vertical, the off hand open. It replaces UAL1 Sword_Idle.
  * mh_combat: the production rig's own combat clips (UAL1/UAL2 sword, hit, death, cast; KayKit bow and spear), the same takes
    retargeted onto the MetaHuman skeleton with maps made from the production ones. Without them a MetaHuman player fell through
    to the production rig's clips, whose bone names it does not have, and was held in its bind pose (arms out: the "T-pose").
Each is retargeted in place (--root inplace). The poses take --stance source (the take's own step width, never the target
rest's), and so do the UAL combat takes; the KayKit bow and spear guards keep --stance rest, as their production clips were made,
because with their own step width they stand about a metre wide.
"""
import argparse
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
DEPOT = r"C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary"
PROD_TARGET = os.path.join(DEPOT, "rigged", "pc_veth_m_body_c", "pc_veth_m_body_c_rigged.glb")
MH_TARGET = r"C:\Otherreach_Recovery\asset_work\metahuman\work\assembled\mh_veth_m\mh_veth_m_rigged.glb"
EXPORTS = r"D:\Otherreach_Depot\UE_Exports"
TAKES = r"D:\Otherreach_Scratch\anim\src"          # anim_fbx_to_glb.py output for the Mixamo-rig takes
PROV = r"D:\Otherreach_Scratch\anim\prov"          # provenance for the zipped packs (from the production clips' records)
UAL1 = (r"C:\Otherreach_External_Assets\Quaternius\Universal Animation Library[Standard].zip",
        "Universal Animation Library[Standard]/Unreal-Godot/UAL1_Standard_RM.glb", "ual1.json")
UAL2 = (r"C:\Otherreach_External_Assets\Quaternius\Universal Animation Library 2[Standard].zip",
        "Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard_RM.glb", "ual2.json")
KAYKIT_RANGED = (r"C:\Otherreach_External_Assets\KayKit\KayKit_Character_Animations_1.1.zip",
                 "KayKit_Character_Animations_1.1/Animations/gltf/Rig_Medium/Rig_Medium_CombatRanged.glb", "kaykit.json")
KAYKIT_MELEE = (KAYKIT_RANGED[0], "KayKit_Character_Animations_1.1/Animations/gltf/Rig_Medium/Rig_Medium_CombatMelee.glb", "kaykit.json")

SAP = os.path.join(EXPORTS, "SampleAnimationPack_anim", "Glb")
ROKOKO = os.path.join(TAKES, "rokoko_cut", "Glb")
# state -> (source GLB or zip, zip member, provenance, clip, map family, category, clip type, loop blend s, right hand, left hand, why)
POSES = {
    "idle": (os.path.join(SAP, "Animations__Unarmed__AS_M_Unarmed_Idle_01.glb"), None, None, "Animation", "ue5_manny", "locomotion",
             "idle", 0.5, "open", "open", "unarmed mocap idle, weight shifts, 6.7 s; narrow stance, relaxed hands"),
    "talk": (os.path.join(SAP, "Animations__Unarmed__AS_M_Unarmed_Gesture_01.glb"), None, None, "Animation", "ue5_manny", "social",
             "idle", 0.6, "open", "open", "one-handed explaining gesture, 4.8 s"),
    "sword_ready": (os.path.join(ROKOKO, "SwordIdleMedium_mixamo.glb"), None, None, "Animation", "mixamo_rig", "combat", "idle", 0.5,
                    "grip_primary", "open", "Rokoko SwordIdleMedium 3-11 s: one-handed sword held low at the side, arms hanging"),
}
# The MetaHuman's combat states: the production rig's takes (clip ids player.cw_player.<state>), same source clip.
# state -> (archive, clip, map family, category, clip type, loop, right hand, left hand)
COMBAT = {
    "sword_attack": (UAL1, "Sword_Attack", "quaternius_ual", "combat", "transition", False, "grip_primary", "open"),
    "sword_attack_1": (UAL1, "Sword_Attack", "quaternius_ual", "combat", "transition", False, "grip_primary", "open"),
    "sword_attack_2": (UAL2, "Sword_Regular_A", "quaternius_ual", "combat", "transition", False, "grip_primary", "open"),
    "sword_attack_3": (UAL2, "Sword_Regular_B", "quaternius_ual", "combat", "transition", False, "grip_primary", "open"),
    "sword_attack_4": (UAL2, "Sword_Regular_C", "quaternius_ual", "combat", "transition", False, "grip_primary", "open"),
    "sword_block": (UAL2, "Sword_Block", "quaternius_ual", "combat", "transition", False, "grip_primary", "open"),
    "cast": (UAL1, "Spell_Simple_Shoot", "quaternius_ual", "magic", "transition", False, "open", "open"),
    "hit": (UAL1, "Hit_Chest", "quaternius_ual", "combat", "react", False, "open", "open"),
    "death": (UAL1, "Death01", "quaternius_ual", "combat", "death", False, "open", "open"),
    "bow_ready": (KAYKIT_RANGED, "Ranged_Bow_Idle", "kaykit_rig_medium", "combat", "idle", True, "open", "grip_primary"),
    "bow_draw": (KAYKIT_RANGED, "Ranged_Bow_Draw", "kaykit_rig_medium", "combat", "transition", False, "open", "grip_primary"),
    "spear_ready": (KAYKIT_MELEE, "Melee_2H_Idle", "kaykit_rig_medium", "combat", "idle", True, "grip_primary", "grip_secondary"),
    "spear_thrust": (KAYKIT_MELEE, "Melee_2H_Attack_Stab", "kaykit_rig_medium", "combat", "transition", False, "grip_primary", "grip_secondary"),
}
RIGS = {"prod": ("cw_player", "prod", "_std"), "mh": ("mh", "mh", "_mh")}


def maps(family, rig):
    """The map file for a source family on a rig: ue5_manny_prod/_mh, mixamo_rig_prod/_mh, quaternius_ual_std/_mh, ..."""
    if family in ("ue5_manny", "mixamo_rig"):
        return f"{family}_{RIGS[rig][1]}.json"
    return f"{family}{RIGS[rig][2]}.json"


def run(cmd, anim_id):
    out = subprocess.run(cmd, capture_output=True, text=True)
    result = [ln for ln in out.stdout.splitlines() if ln.startswith("RETARGET_RESULT ")]
    if out.returncode or not result:
        sys.exit(f"{anim_id}: retarget failed\n{out.stdout[-3000:]}\n{out.stderr[-2000:]}")
    return json.loads(result[0][len("RETARGET_RESULT "):])


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--out", required=True)
    p.add_argument("--only", default="")
    p.add_argument("--rigs", default="prod,mh,mh_combat")
    a = p.parse_args()
    only = set(filter(None, a.only.split(",")))
    targets = {"prod": PROD_TARGET, "mh": MH_TARGET}
    jobs = []
    for rig in a.rigs.split(","):
        if rig == "mh_combat":
            for st, ((archive, member, prov), clip, family, cat, ctype, loop, right, left) in COMBAT.items():
                jobs.append(("mh", f"player.mh.{st}", archive, member, os.path.join(PROV, prov), clip, family, cat, ctype, loop, 0.0,
                             right, left, "rest" if family == "kaykit_rig_medium" else "source", f"{clip}, the take player.cw_player.{st} plays on the production rig"))
        else:
            for st, (src, member, prov, clip, family, cat, ctype, blend, right, left, why) in POSES.items():
                jobs.append((rig, f"player.{RIGS[rig][0]}.{st}", src, member, prov, clip, family, cat, ctype, True, blend, right, left,
                             "source", why))
    report = {}
    for rig, anim_id, src, member, prov, clip, family, cat, ctype, loop, blend, right, left, stance, why in jobs:
        if only and anim_id.split(".")[-1] not in only:
            continue
        cmd = [BLENDER, "--background", "--factory-startup", "--python", os.path.join(HERE, "_retarget_clip.py"), "--",
               "--source", src, "--clip", clip, "--map", os.path.join(HERE, "retarget_maps", maps(family, rig)),
               "--target", targets[rig], "--id", anim_id, "--root", "inplace", "--stance", stance,
               "--clip-type", ctype, "--category", cat, "--hand-right", right, "--hand-left", left,
               "--assets", a.out, "--tag", "stance_fix", "--notes", f"{why}; retargeted in place, --stance {stance}."]
        if member:
            cmd += ["--member", member]
        if prov:
            cmd += ["--provenance", prov]
        if loop:
            cmd += ["--loop", "--loop-blend", str(blend)]
        res = run(cmd, anim_id)
        report[anim_id] = {"source": f"{os.path.basename(src)}:{member or ''}:{clip}", "glb": res["glb"], "duration_s": res["duration_s"]}
        print(anim_id, json.dumps(report[anim_id]), flush=True)
    with open(os.path.join(a.out, f"stance_report_{'_'.join(sorted(only)) or 'all'}.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1)


if __name__ == "__main__":
    main()
