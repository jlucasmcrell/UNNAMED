"""The player's walk / run / sprint from the free Fab motion-capture packs, on both player skeletons (2026-10-16).

    python locomotion_fab.py --exports D:\\Otherreach_Depot\\UE_Exports --out D:\\Otherreach_Scratch\\locomotion\\staging
        [--prod-target <pc_veth_m_body_c_rigged.glb>] [--mh-target <mh_veth_m_rigged.glb>] [--only walk,run]

Each clip: ue_export/export_pack.py --animations (UE 5.8, ExportHost) -> ue_export/anim_fbx_to_glb.py -> _retarget_clip.py
(--root inplace --stance source: the source's own step width, not the target rest's) onto
  * the production rig (the MPFB cast: pc_veth_m_body_c and every production NPC) as anim.player.cw_player.<state>, map
    retarget_maps/ue5_manny_prod.json;
  * the MetaHuman game skeleton (mh_veth_m; mh_veth_f carries it rest-relative) as anim.player.mh.<state>, map
    retarget_maps/ue5_manny_mh.json.
Plus NPC idle variety and talk clips on the production rig as anim.npc.cw_npc.<state> (NPC_CLIPS; takes that do not loop
are eased into their first frame, --loop-blend). Then each locomotion record gets authored_speed_m_s (the source's root speed on this body: the ground speed at which the mocap foot
stays planted) and the gait facts from clip_gait.py, and gait_report.json lists them all with the paces for the bindings.
"""
import argparse
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from clip_gait import analyse  # noqa: E402

BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
DEPOT = r"C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary"
PROD_TARGET = os.path.join(DEPOT, "rigged", "pc_veth_m_body_c", "pc_veth_m_body_c_rigged.glb")
MH_TARGET = r"C:\Otherreach_Recovery\asset_work\metahuman\work\assembled\mh_veth_m\mh_veth_m_rigged.glb"

# state -> (pack folder under --exports, clip key, why)
CLIPS = {
    "walk": ("SampleAnimationPack_anim", "Animations__Unarmed__AS_M_Unarmed_Walk_F_Loop",
             "male unarmed walk, 120 fps mocap, three strides (3.78 s), 1.25 m/s, 0.21 m ankle width at mid-stance"),
    "run": ("FreeSampleAnimationSet_anim", "Animations__FemaleLocomotionSet__Mannequin__RootMotion__A_JogFwd_Loop",
            "jog, 2.9 m/s (the game's 3.2 m/s run at ~1.07x), narrow running stance"),
    "sprint": ("FreeSampleAnimationSet_anim", "Animations__MaleLocomotionSet__Mannequin__RootMotion__A_RunFwd_Loop",
               "male run, 5.9 m/s (the game's 5.12 m/s sprint at ~0.85x)"),
}
# NPC idle variety and talk (production cast only): state -> (pack, clip key, category, loop blend s, why)
NPC_CLIPS = {
    "idle_a": ("SampleAnimationPack_anim", "Animations__Unarmed__AS_M_Unarmed_Idle_01", "locomotion", 0.5,
               "unarmed standing idle, weight shifts, 6.7 s"),
    "idle_b": ("FreeAnimationLibrary_anim", "Animations__Idle__anim_Idle", "locomotion", 0.5, "relaxed standing idle, 5.7 s"),
    "talk_a": ("SampleAnimationPack_anim", "Animations__Unarmed__AS_M_Unarmed_Gesture_01", "social", 0.6,
               "one-handed explaining gesture, 4.8 s"),
    "talk_b": ("FreeAnimationsPack_anim", "Animations__AS_Talk", "social", 1.2,
               "two-handed conversation, 14.1 s; ends with the hands raised, eased back into the start over 1.2 s"),
}
RIGS = {
    "prod": ("cw_player", "ue5_manny_prod.json", ("foot.L", "foot.R"), ("thigh.L", "thigh.R")),
    "mh": ("mh", "ue5_manny_mh.json", ("foot_l", "foot_r"), ("thigh_l", "thigh_r")),
}


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--exports", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--prod-target", default=PROD_TARGET)
    p.add_argument("--mh-target", default=MH_TARGET)
    p.add_argument("--only", default="")
    p.add_argument("--rigs", default="prod,mh,npc")
    a = p.parse_args()
    targets = {"prod": a.prod_target, "mh": a.mh_target}
    report = {}
    jobs = []
    for rig in a.rigs.split(","):
        if rig == "npc":
            jobs += [("prod", f"npc.cw_npc.{st}", pk, ky, cat, blend, why) for st, (pk, ky, cat, blend, why) in NPC_CLIPS.items()
                     if not a.only or st in a.only.split(",")]
        else:
            jobs += [(rig, f"player.{RIGS[rig][0]}.{st}", pk, ky, "locomotion", 0.0, why) for st, (pk, ky, why) in CLIPS.items()
                     if not a.only or st in a.only.split(",")]
    for rig, anim_id, pack, key, category, blend, why in jobs:
        _family, map_name, feet, thighs = RIGS[rig]
        source = os.path.join(a.exports, pack, "Glb", key + ".glb")
        cmd = [BLENDER, "--background", "--factory-startup", "--python", os.path.join(HERE, "_retarget_clip.py"), "--",
               "--source", source, "--clip", "Animation", "--map", os.path.join(HERE, "retarget_maps", map_name),
               "--target", targets[rig], "--id", anim_id, "--root", "inplace", "--stance", "source", "--loop",
               "--clip-type", "idle" if anim_id.startswith("npc.") else "move", "--category", category,
               "--loop-blend", str(blend), "--assets", a.out, "--tag", "fab_mocap",
               "--notes", f"{key} ({why}), retargeted in place with the source's step width (--stance source)."]
        run = subprocess.run(cmd, capture_output=True, text=True)
        result = [ln for ln in run.stdout.splitlines() if ln.startswith("RETARGET_RESULT ")]
        if run.returncode or not result:
            sys.exit(f"{anim_id}: retarget failed\n{run.stdout[-3000:]}\n{run.stderr[-2000:]}")
        res = json.loads(result[0][len("RETARGET_RESULT "):])
        rec_path = res["record"]
        with open(rec_path, encoding="utf-8") as fh:
            record = json.load(fh)
        gait = analyse(res["glb"], list(feet), list(thighs))
        source_gait = analyse(source, ["foot_l", "foot_r"], ["thigh_l", "thigh_r"])
        if not anim_id.startswith("npc."):
            record["authored_speed_m_s"] = record["retarget"]["root_speed_m_s"]
        record["gait"] = {k: gait[k] for k in ("stance_mid_m", "stance_mean_m", "rest_ankle_sep_m", "hip_width_m",
                                               "pace_m_s", "loop_seam_rot_deg", "loop_seam_ankle_m")}
        record["gait"]["source_stance_mid_m"] = source_gait["stance_mid_m"]
        record["gait"]["method"] = "tools/asset_pipeline/clip_gait.py (ankle joints, lateral axis = the rest thighs)"
        with open(rec_path, "w", encoding="utf-8") as fh:
            json.dump(record, fh, indent=2)
            fh.write("\n")
        report[anim_id] = {"source": f"{pack}/{key}", "why": why, "glb": res["glb"], "record": rec_path,
                           "authored_speed_m_s": record.get("authored_speed_m_s"), "scale": res["scale"],
                           "loop_closure_max_deg": record["retarget"]["loop_closure_max_deg"],
                           "duration_s": res["duration_s"], **record["gait"]}
        print(anim_id, json.dumps(report[anim_id]))
    with open(os.path.join(a.out, "gait_report.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1)


if __name__ == "__main__":
    main()
