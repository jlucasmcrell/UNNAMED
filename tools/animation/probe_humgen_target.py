"""Inspect a paid HumGen Godot export with Epic's native GLTF importer and IK Rig API."""
import hashlib
import json
import os
import time
import traceback
from pathlib import Path

import unreal

root = Path(os.environ["OTHERREACH_HUMGEN_ANIMATION_JOB"])
source = Path(os.environ["OTHERREACH_HUMGEN_TARGET_GLB"])
target_id = os.environ.get("OTHERREACH_HUMGEN_TARGET_ID", "probe_ada")
source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
destination = "/Game/HumGenMotion/ProductionAdaProbe" if target_id == "probe_ada" else "/Game/HumGenMotion/Targets/" + target_id + "_" + source_hash[:8]
report = root / ("target_probe.json" if target_id == "probe_ada" else "target_" + target_id + ".json")
state = {"source": str(source), "body_sha256": source_hash}
started = time.monotonic()

try:
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    existing = unreal.EditorAssetLibrary.list_assets(destination, recursive=True, include_folder=False)
    if not any("_Body_LOD3." in path for path in existing):
        task = unreal.AssetImportTask()
        task.filename = str(source)
        task.destination_path = destination
        task.automated = True
        task.save = True
        task.replace_existing = bool(existing)  # Retry an owned partial import after owner-play interruption.
        tools.import_asset_tasks([task])
    bodies = []
    for path in unreal.EditorAssetLibrary.list_assets(destination, recursive=True, include_folder=False):
        mesh = unreal.load_asset(path)
        if not isinstance(mesh, unreal.SkeletalMesh):
            continue
        if target_id != "probe_ada" and "_Body_LOD3." not in mesh.get_path_name():
            continue
        component = unreal.new_object(unreal.SkeletalMeshComponent)
        component.set_skeletal_mesh_asset(mesh)
        bones = [{"name": str(component.get_bone_name(i)),
                  "parent": str(component.get_parent_bone(component.get_bone_name(i)))}
                 for i in range(component.get_num_bones())]
        rig = tools.create_asset("IK_" + mesh.get_name(), destination, None, unreal.IKRigDefinitionFactory())
        if rig is None:
            rig = unreal.load_asset(destination + "/IK_" + mesh.get_name())
        controller = unreal.IKRigController.get_controller(rig)
        assert controller.set_skeletal_mesh(mesh)
        controller.apply_auto_generated_retarget_definition()
        bodies.append({"asset": mesh.get_path_name(), "bones": bones,
                       "rig": rig.get_path_name(), "root": str(controller.get_retarget_root()),
                       "chains": [{"name": str(c.chain_name),
                                   "start": str(controller.get_retarget_chain_start_bone(c.chain_name)),
                                   "end": str(controller.get_retarget_chain_end_bone(c.chain_name)),
                                   "goal": str(controller.get_retarget_chain_goal(c.chain_name))}
                                  for c in controller.get_retarget_chains()]})
    state["bodies"] = bodies
    state["ik_api"] = {name: getattr(unreal.IKRigController, name).__doc__
                       for name in dir(unreal.IKRigController)
                       if any(term in name for term in ("goal", "solver", "retarget", "root_motion")) and not name.startswith("_")}
    options = unreal.GLTFExportOptions()
    state["export_api"] = {name: str(getattr(options, name)) for name in dir(options)
                           if not name.startswith("_") and any(word in name for word in ("material", "mesh", "animation", "skin", "level"))}
    assert bodies, "Native importer produced no skeletal mesh"
    assert any(len(body["bones"]) == 59 for body in bodies), "Paid HumGen native skeleton must remain 59 bones"
    unreal.EditorAssetLibrary.save_directory(destination, only_if_is_dirty=True, recursive=True)
    state["status"] = "DONE"
except Exception:
    state.update(status="FAILED", error=traceback.format_exc())
finally:
    state["minutes"] = (time.monotonic() - started) / 60
    report.write_text(json.dumps(state, indent=2), encoding="utf-8")
    unreal.SystemLibrary.quit_editor()
