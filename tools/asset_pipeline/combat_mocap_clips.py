"""Grounded sword, spear and bow clips from the mocap we already own, on both player skeletons (production cw_player and MetaHuman).

    python combat_mocap_clips.py --out D:\\Otherreach_Scratch\\anim\\staging_combat [--only sword_attack_1,bow_draw] [--rigs prod,mh] [--cut-only]

2026-10-17, the owner's "realistic, grounded" pass after stance_clips.py: the UAL attacks and the KayKit bow and spear guards stand
0.5-0.65 m wide at ready and about 1 m wide mid-lunge. Every state here keeps its game contract (state names, the 0.45 / 0.7 / 1 attack marks:
the contact frame of each take is cut to land at 0.5-0.6 of the clip) and gets a NEW clip id, `player.<rig>.<state>_mc`, so the bindings
choose between the old and the new set and the old clips stay installed as the rollback.

Sources (all owned, none fed to generative AI):
  * Rokoko free mocap 263 (commercial game use allowed, LICENCE.txt): Sword_Generic and Sword_Samurai (one-handed sword work, cut to
    single strikes), Hawkeye_Archery (bow held, then drawn).
  * Mixamo (owner/Claude ruling 2026-10-17: allowed in games): Great_Sword_Blocking, Hit_Reaction_2, Dying_1, Shooting_Arrow.
  * Fab FreeAnimationLibrary (Update_2 spear set): Spear_Idle and Spear_Attack_06. Its export records the licence as UNVERIFIED; it is
    treated as Fab Standard like the Sample Animation Pack until the listing is read - confirm before the clips are installed.

Each take is cut with anim_fbx_to_glb.py --frames into its own GLB under <scratch>/anim/src2/cuts, then retargeted in place with
_retarget_clip.py: --stance source (the take's own step width), --pin-hips (a step happens under the body instead of carrying it away) and,
for the spear (a 0.66 m guard), --stance-narrow.
"""
import argparse
import json
import os
import shutil
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BLENDER = r"C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
DEPOT = r"C:\Otherreach_Recovery\Otherreach_Playtest_RESTORE_20261015b\primary"
PROD_TARGET = os.path.join(DEPOT, "rigged", "pc_veth_m_body_c", "pc_veth_m_body_c_rigged.glb")
# the rig the game ships (game_ws fallback, 2026-10-16): the later work\assembled rig carries corrective joints the maps do not declare
MH_TARGET = r"C:\Otherreach_Recovery\asset_work\metahuman\game_ws\fallback\rigged\mh_veth_m\mh_veth_m_rigged.glb"
CUTS = r"D:\Otherreach_Scratch\anim\src2\cuts"
ROKOKO = r"D:\Otherreach_Depot\Rokoko_Free_263\Rokoko Studio (Mocap)"
MIXAMO = r"D:\Otherreach_Depot\Mixamo_20261017"
FAL = r"D:\Otherreach_Depot\UE_Exports\FreeAnimationLibrary_anim\Animations"
PROV_FROM = r"D:\Otherreach_Scratch\anim\src"
CONVERTER = os.path.join(HERE, "ue_export", "anim_fbx_to_glb.py")

PROVENANCE = {
    "rokoko": os.path.join(PROV_FROM, "rokoko_cut", "Glb", "provenance.json"),
    "mixamo": os.path.join(PROV_FROM, "mixamo", "Glb", "provenance.json"),
    "fal": {
        "id": "fab__free_animation_library",
        "pack_name": "Free Animation Library (Fab, UE5 mannequin; Update_2 spear set)",
        "author_provider": "Fab seller of the Free Animation Library (the AS_ Update_2 clips; seller name not readable here)",
        "license": "UNVERIFIED in the export record; treated as Fab Standard (any engine) like the Sample Animation Pack until the listing is "
                   "read. NEVER fed to generative AI, AI training or AI judges. Confirm before the clips are installed.",
        "license_url": "https://www.fab.com/eula",
        "url": "https://www.fab.com/",
        "source_project": "C:/Otherreach_External_Assets/Fab/FabAssets/Content/FreeAnimationLibrary -> ExportHost",
        "export": "tools/asset_pipeline/ue_export/export_pack.py --animations (UE 5.8.3) + anim_fbx_to_glb.py (Blender 5.2)",
    },
}

# cut name -> (pack, source FBX, frames a:b or None). Rokoko 24 fps, Mixamo 30 fps, FAL 30 fps; frame = seconds * fps + 1.
CUT_TABLE = {
    "mc_sword_atk1": ("rokoko", os.path.join(ROKOKO, "RokokoTVContest_MocapAssets", "COMBAT", "MIXAMO", "Sword_Samurai_mixamo.fbx"), "593:622"),
    "mc_sword_atk2": ("rokoko", os.path.join(ROKOKO, "RokokoTVContest_MocapAssets", "COMBAT", "MIXAMO", "Sword_Samurai_mixamo.fbx"), "239:267"),
    "mc_sword_atk3": ("rokoko", os.path.join(ROKOKO, "RokokoTVContest_MocapAssets", "COMBAT", "MIXAMO", "Sword_Generic_mixamo.fbx"), "222:251"),
    "mc_sword_atk4": ("rokoko", os.path.join(ROKOKO, "RokokoTVContest_MocapAssets", "COMBAT", "MIXAMO", "Sword_Generic_mixamo.fbx"), "394:426"),
    "mc_bow_ready": ("rokoko", os.path.join(ROKOKO, "Superhero", "Hawkeye_Archery_mixamo.fbx"), "10:84"),
    "mc_sword_block": ("mixamo", os.path.join(MIXAMO, "Great_Sword_Blocking.fbx"), None),
    "mc_hit": ("mixamo", os.path.join(MIXAMO, "Hit_Reaction_2.fbx"), None),
    "mc_death": ("mixamo", os.path.join(MIXAMO, "Dying_1.fbx"), None),
    "mc_bow_draw": ("mixamo", os.path.join(MIXAMO, "Shooting_Arrow.fbx"), "1:69"),
    "mc_spear_ready": ("fal", os.path.join(FAL, "Animations__Update_2__Spear__AS_Spear_Idle.fbx"), None),
    "mc_spear_thrust": ("fal", os.path.join(FAL, "Animations__Update_2__Spear__AS_Spear_Attack_06.fbx"), None),
}

# state -> (cut, map family, category, clip type, loop, loop blend s, right hand, left hand, narrow, pin hips, why)
STATES = {
    "sword_attack_1": ("mc_sword_atk1", "mixamo_rig", "combat", "transition", False, 0.0, "grip_primary", "open", 1.0, 1.0,
                       "Rokoko Sword_Samurai 24.7-25.9 s: a one-handed cut from the ready guard, feet 0.33 m apart"),
    "sword_attack_2": ("mc_sword_atk2", "mixamo_rig", "combat", "transition", False, 0.0, "grip_primary", "open", 1.0, 1.0,
                       "Rokoko Sword_Samurai 9.9-11.1 s: a one-handed cut, feet 0.43 m apart"),
    "sword_attack_3": ("mc_sword_atk3", "mixamo_rig", "combat", "transition", False, 0.0, "grip_primary", "open", 1.0, 1.0,
                       "Rokoko Sword_Generic 9.25-10.45 s: a stepping rising cut"),
    "sword_attack_4": ("mc_sword_atk4", "mixamo_rig", "combat", "transition", False, 0.0, "grip_primary", "open", 1.0, 1.0,
                       "Rokoko Sword_Generic 16.4-17.75 s: a stepping cut"),
    "sword_block": ("mc_sword_block", "mixamo_rig", "combat", "transition", False, 0.0, "grip_primary", "open", 1.0, 1.0,
                    "Mixamo Great_Sword_Blocking: an upright block, feet 0.11 m apart"),
    "hit": ("mc_hit", "mixamo_rig", "combat", "react", False, 0.0, "open", "open", 1.0, 1.0,
            "Mixamo Hit_Reaction_2: a flinch on a narrow stance"),
    "death": ("mc_death", "mixamo_rig", "combat", "death", False, 0.0, "open", "open", 1.0, 1.0,
              "Mixamo Dying_1: a stagger and fall"),
    "bow_ready": ("mc_bow_ready", "mixamo_rig", "combat", "idle", True, 0.5, "open", "grip_primary", 1.0, 1.0,
                  "Rokoko Hawkeye_Archery 0.4-3.5 s: the bow held low and ready, feet 0.36 m apart"),
    "bow_draw": ("mc_bow_draw", "mixamo_rig", "combat", "transition", False, 0.0, "open", "grip_primary", 1.0, 1.0,
                 "Mixamo Shooting_Arrow 0-2.3 s: draw, release at 0.52 of the clip, back to the nock"),
    "spear_ready": ("mc_spear_ready", "ue5_manny", "combat", "idle", True, 0.5, "grip_primary", "grip_secondary", 0.55, 1.0,
                    "Fab FreeAnimationLibrary AS_Spear_Idle, ankles brought in from 0.66 m"),
    "spear_thrust": ("mc_spear_thrust", "ue5_manny", "combat", "transition", False, 0.0, "grip_primary", "grip_secondary", 0.55, 1.0,
                     "Fab FreeAnimationLibrary AS_Spear_Attack_06: a thrust at 0.58 of the clip, ankles brought in from 0.7 m"),
}
RIGS = {"prod": ("cw_player", "prod", PROD_TARGET), "mh": ("mh", "mh", MH_TARGET)}


def cut(name, only_missing=True):
    pack, fbx, frames = CUT_TABLE[name]
    glb = os.path.join(CUTS, pack, "Glb", name + ".glb")
    if only_missing and os.path.exists(glb):
        return glb
    stage = os.path.join(CUTS, "_in", name)
    shutil.rmtree(stage, ignore_errors=True)
    os.makedirs(os.path.join(stage, "Animations"))
    shutil.copy(fbx, os.path.join(stage, "Animations", name + ".fbx"))
    out = os.path.join(CUTS, pack, "Glb")
    os.makedirs(out, exist_ok=True)
    cmd = [BLENDER, "--background", "--factory-startup", "--python", CONVERTER, "--", stage, "--out", out]
    if frames:
        cmd += ["--frames", frames]
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode or not os.path.exists(glb):
        sys.exit(f"{name}: cut failed\n{r.stdout[-2000:]}\n{r.stderr[-1000:]}")
    prov = PROVENANCE[pack]
    dst = os.path.join(out, "provenance.json")
    if isinstance(prov, dict):
        with open(dst, "w", encoding="utf-8") as fh:
            json.dump(prov, fh, indent=1)
    else:
        shutil.copy(prov, dst)
    return glb


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
    p.add_argument("--rigs", default="prod,mh")
    p.add_argument("--cut-only", action="store_true")
    p.add_argument("--recut", action="store_true")
    a = p.parse_args()
    only = set(filter(None, a.only.split(",")))
    report = {}
    for st, (cutname, family, cat, ctype, loop, blend, right, left, narrow, pin, why) in STATES.items():
        if only and st not in only:
            continue
        glb = cut(cutname, not a.recut)
        if a.cut_only:
            print("cut", cutname, glb, flush=True)
            continue
        for rig in a.rigs.split(","):
            tag, key, target = RIGS[rig]
            anim_id = f"player.{tag}.{st}_mc"
            map_name = f"{family}_{key}.json" if family in ("ue5_manny", "mixamo_rig") else f"{family}_{key}.json"
            cmd = [BLENDER, "--background", "--factory-startup", "--python", os.path.join(HERE, "_retarget_clip.py"), "--",
                   "--source", glb, "--clip", "Animation", "--map", os.path.join(HERE, "retarget_maps", map_name),
                   "--target", target, "--id", anim_id, "--root", "inplace", "--stance", "source",
                   "--stance-narrow", str(narrow), "--pin-hips", str(pin),
                   "--clip-type", ctype, "--category", cat, "--hand-right", right, "--hand-left", left,
                   "--assets", a.out, "--tag", "combat_mocap",
                   "--notes", f"{why}; retargeted in place, --stance source, --pin-hips {pin}, --stance-narrow {narrow}."]
            if loop:
                cmd += ["--loop", "--loop-blend", str(blend)]
            res = run(cmd, anim_id)
            report[anim_id] = {"source": f"{cutname}", "glb": res["glb"], "duration_s": res["duration_s"]}
            print(anim_id, json.dumps(report[anim_id]), flush=True)
    if not a.cut_only:
        name = "combat_report_" + ("_".join(sorted(only)) or "all") + ".json"
        os.makedirs(a.out, exist_ok=True)
        with open(os.path.join(a.out, name), "w", encoding="utf-8") as fh:
            json.dump(report, fh, indent=1)


if __name__ == "__main__":
    main()
