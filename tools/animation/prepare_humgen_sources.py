"""Import the original CC0 UAL files into the clean HumGen animation project."""
import hashlib
import json
import os
import traceback
from pathlib import Path
import unreal

root = Path(os.environ["OTHERREACH_HUMGEN_ANIMATION_JOB"])
original = json.loads(Path("D:/Otherreach_Scratch/rcd3_20261020/evidence/ue_animation_sources.json").read_text())
state = {"sources": []}
try:
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    for source in original["sources"]:
        if source["id"] not in ("ual1", "ual2"):
            continue
        folder = "/Game/HumGenSources/" + source["id"]
        if not unreal.EditorAssetLibrary.list_assets(folder, recursive=True, include_folder=False):
            task = unreal.AssetImportTask()
            task.filename = source["path"]
            task.destination_path = folder
            task.automated = True
            task.save = True
            tools.import_asset_tasks([task])
        assets = []
        for path in unreal.EditorAssetLibrary.list_assets(folder, recursive=True, include_folder=False):
            item = unreal.load_asset(path)
            if isinstance(item, unreal.AnimSequence):
                assets.append({"kind": "animation", "asset": item.get_path_name(), "name": item.get_name()})
            elif isinstance(item, unreal.SkeletalMesh):
                assets.append({"kind": "mesh", "asset": item.get_path_name()})
        assert any(a["kind"] == "animation" for a in assets)
        state["sources"].append({"id": source["id"], "path": source["path"], "sha256": hashlib.sha256(Path(source["path"]).read_bytes()).hexdigest(), "assets": assets})
        unreal.EditorAssetLibrary.save_directory(folder, only_if_is_dirty=True, recursive=True)
    state["status"] = "DONE"
except Exception:
    state.update(status="FAILED", error=traceback.format_exc())
(root / "sources.json").write_text(json.dumps(state, indent=2), encoding="utf-8")
unreal.SystemLibrary.quit_editor()
