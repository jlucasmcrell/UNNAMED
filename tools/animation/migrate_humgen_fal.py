"""Epic native migration of already-acquired FAL sources into the HumGen-only project."""
import hashlib
import json
import os
import traceback
from pathlib import Path
import unreal

root = Path(os.environ["OTHERREACH_HUMGEN_ANIMATION_JOB"])
source = Path("C:/Otherreach_External_Assets/Fab/FabAssets/Content")
target = root / "project/Content"
assets = ["/Game/FreeAnimationLibrary/Demo/Characters/Mannequins/Meshes/SKM_Manny_Simple"]
for folder, names in {
    "Walk": ["anim_Walk_Left_Loop", "anim_Walk_Right_Loop", "anim_Walk_Bwd_Loop_L"],
    "Jog": ["anim_Jog_Loop_Left_L", "anim_Jog_Loop_Right_R", "anim_Jog_Loop_Bwd"],
    "Update_2/Spear": ["AS_Spear_Idle", "AS_Spear_Attack_01"],
    "LandingRoll": ["anim_LandRoll_R"],
}.items():
    assets += ["/Game/FreeAnimationLibrary/Animations/" + folder + "/" + name for name in names]
state = {"packages": assets}
try:
    hashes = {p: hashlib.sha256((source / (p.removeprefix("/Game/") + ".uasset")).read_bytes()).hexdigest() for p in assets}
    options = unreal.MigrationOptions()
    options.prompt = False
    options.ignore_dependencies = False
    options.asset_conflict = unreal.AssetMigrationConflict.SKIP
    unreal.AssetToolsHelpers.get_asset_tools().migrate_packages(assets, str(target), options)
    assert all(hashlib.sha256((source / (p.removeprefix("/Game/") + ".uasset")).read_bytes()).hexdigest() == digest for p, digest in hashes.items())
    assert all((target / (p.removeprefix("/Game/") + ".uasset")).exists() for p in assets)
    state.update(status="DONE", original_sha256=hashes)
except Exception:
    state.update(status="FAILED", error=traceback.format_exc())
(root / "fal_migration.json").write_text(json.dumps(state, indent=2), encoding="utf-8")
unreal.SystemLibrary.quit_editor()
