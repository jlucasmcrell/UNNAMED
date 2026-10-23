"""Owner source policy: no KayKit for player/NPC locomotion (2026-10-23)."""
LOCOMOTION = {
    "walk_strafe_left": ("fal", "anim_Walk_Left_Loop"),
    "walk_strafe_right": ("fal", "anim_Walk_Right_Loop"),
    "strafe_left": ("fal", "anim_Jog_Loop_Left_L"),
    "strafe_right": ("fal", "anim_Jog_Loop_Right_R"),
    "walk_back": ("fal", "anim_Walk_Bwd_Loop_L"),
    "run_back": ("fal", "anim_Jog_Loop_Bwd"),
}

def epic_source():
    assets = [{"kind": "mesh", "asset": "/Game/FreeAnimationLibrary/Demo/Characters/Mannequins/Meshes/SKM_Manny_Simple.SKM_Manny_Simple"}]
    for _, name in LOCOMOTION.values():
        folder = "Walk" if name.startswith("anim_Walk") else "Jog"
        assets.append({"kind": "animation", "name": name, "asset": f"/Game/FreeAnimationLibrary/Animations/{folder}/{name}.{name}"})
    return {"id": "fal", "path": "FreeAnimationLibrary", "assets": assets}

if __name__ == "__main__":
    assert len(LOCOMOTION) == 6 and all(source == "fal" for source, _ in LOCOMOTION.values())
    assert not {"walk", "run", "sprint"}.intersection(LOCOMOTION), "Keep Quaternius forward gait sources"
    assert len(epic_source()["assets"]) == 7
    print("MOCAP_SOURCE_POLICY_PASS")
