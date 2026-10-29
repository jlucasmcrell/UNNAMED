"""Approved-source motion probe on the unchanged paid HumGen rig, using Epic IK Retargeter."""
import json
import hashlib
import os
import struct
import time
import traceback
from pathlib import Path
import unreal

root = Path(os.environ["OTHERREACH_HUMGEN_ANIMATION_JOB"])
target_id = os.environ.get("OTHERREACH_HUMGEN_TARGET_ID", "probe_ada")
report = root / ("retarget_probe.json" if target_id == "probe_ada" else "retarget_" + target_id + ".json")
state = {"clips": []}
tools = unreal.AssetToolsHelpers.get_asset_tools()
package = "/Game/HumGenMotion/ProductionAdaMotionProbe" if target_id == "probe_ada" else "/Game/HumGenMotion/Motion/" + target_id
selected = {"idle": ("ual1", "Idle_Loop"), "walk": ("ual1", "Walk_Loop"),
            "walk_strafe_left": ("fal", "anim_Walk_Left_Loop"), "sword_attack_1": ("ual1", "Sword_Attack")}
if os.environ.get("OTHERREACH_HUMGEN_SOURCE_SELECTION"):
    selected = json.loads(Path(os.environ["OTHERREACH_HUMGEN_SOURCE_SELECTION"]).read_text())
if os.environ.get('OTHERREACH_HUMGEN_EXTRA_ONLY') == '1':
    extra_ids = {row['id'] for row in json.loads(Path(os.environ['OTHERREACH_HUMGEN_EXTRA_SOURCES']).read_text())['sources']}
    selected = {key: value for key, value in selected.items() if value[0] in extra_ids}
state.update(target_id=target_id, selection_sha256=hashlib.sha256(json.dumps(selected, sort_keys=True).encode()).hexdigest(),
             retargeter_script_sha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest())

def save(status, **values):
    state.update(status=status, **values)
    if status in ('EXPORTED', 'RETARGETING'):
        (root / ('retarget_progress_' + target_id + '.json')).write_text(
            json.dumps(dict(status=status, current=values.get('current'), exported=len(state['clips']), selected=len(selected))), encoding='utf-8')
        if len(state['clips']) % 50:
            return
    report.write_text(json.dumps(state, indent=2, default=str), encoding="utf-8")

def asset(folder, name, factory):
    return unreal.load_asset(folder + "/" + name) or tools.create_asset(name, folder, None, factory)

def chains(controller):
    return [{"name": str(c.chain_name), "start": str(controller.get_retarget_chain_start_bone(c.chain_name)),
             "end": str(controller.get_retarget_chain_end_bone(c.chain_name)),
             "goal": str(controller.get_retarget_chain_goal(c.chain_name))} for c in controller.get_retarget_chains()]

started = time.monotonic()
try:
    native = json.loads(Path(os.environ.get("OTHERREACH_HUMGEN_TARGET_REPORT", str(root / "target_probe.json"))).read_text())
    assert native["status"] == "DONE"
    target_info = next(b for b in native["bodies"] if "_Body_LOD3." in b["asset"])
    body = unreal.load_asset(target_info["asset"])
    bones = [b["name"] for b in target_info["bones"]]
    assert len(bones) == 59 and all(b in bones for b in ("Hips", "LeftUpperArm", "LeftLowerArm", "RightUpperLeg"))
    target = asset(package, "IK_Target", unreal.IKRigDefinitionFactory())
    target_controller = unreal.IKRigController.get_controller(target)
    assert target_controller.set_skeletal_mesh(body)
    target_controller.apply_auto_generated_retarget_definition()
    assert target_controller.set_retarget_root("Hips")
    assert target_controller.set_root_motion_bone("Root")
    # Auto-characterization recognizes the hands/feet but misses this rig's upper limb
    # names, producing one-bone arm/leg chains. Correct the IK definitions, not the rig.
    for name, first, last in [("Spine", "Spine", "UpperChest"), ("LeftArm", "LeftUpperArm", "LeftHand"),
                               ("RightArm", "RightUpperArm", "RightHand"),
                               ("LeftLeg", "LeftUpperLeg", "LeftFoot"), ("RightLeg", "RightUpperLeg", "RightFoot")]:
        assert target_controller.set_retarget_chain_start_bone(name, first)
        assert target_controller.set_retarget_chain_end_bone(name, last)
    for side in ("Left", "Right"):
        if not any(str(c.chain_name) == side + "Foot" for c in target_controller.get_retarget_chains()):
            target_controller.add_retarget_chain(side + "Foot", side + "Toes", side + "Toes", "None")
    for chain in list(target_controller.get_retarget_chains()):
        if any(digit in str(target_controller.get_retarget_chain_start_bone(chain.chain_name))
               for digit in ("Thumb", "Index", "Middle", "Ring", "Little")):
            target_controller.remove_retarget_chain(chain.chain_name)
    state.update(target=body.get_path_name(), body_sha256=native["body_sha256"], bones=target_info["bones"],
                 target_chains=chains(target_controller), solvers=target_controller.get_num_solvers())
    sources = json.loads((root / "sources.json").read_text())
    assert sources["status"] == "DONE"
    fal = json.loads((root / "fal_migration.json").read_text())
    assert fal["status"] == "DONE"
    sources["sources"].append({"id": "fal", "path": "FreeAnimationLibrary", "assets":
        [{"kind": "mesh", "asset": "/Game/FreeAnimationLibrary/Demo/Characters/Mannequins/Meshes/SKM_Manny_Simple.SKM_Manny_Simple"}]
        + [{"kind": "animation", "name": Path(p).name, "asset": p + "." + Path(p).name}
           for p in fal["packages"] if "/Animations/" in p]})
    sources["sources"].append(dict(sources["sources"][-1], id="fal_spear"))
    sources["sources"].append(dict(sources["sources"][-1], id="fal_side_walk"))
    if os.environ.get("OTHERREACH_HUMGEN_EXTRA_SOURCES"):
        extra = json.loads(Path(os.environ["OTHERREACH_HUMGEN_EXTRA_SOURCES"]).read_text())
        assert extra["status"] == "DONE"
        sources["sources"].extend(extra["sources"])
    # No textures/material baking is needed for an animation transport preview. The game
    # keeps the original, dressed HumGen GLB; these material slots never go into the game.
    materials = body.get_editor_property("materials")
    for material in materials:
        material.set_editor_property("material_interface", None)
    body.set_editor_property("materials", materials)
    for source in sources["sources"]:
        sid = source["id"]
        todo = {key: name for key, (provider, name) in selected.items() if provider == sid}
        if not todo:
            continue
        assert "kaykit" not in sid.lower()
        source_mesh = unreal.load_asset(next(a["asset"] for a in source["assets"] if a["kind"] == "mesh"))
        rig = asset(package, "IK_Source_" + sid, unreal.IKRigDefinitionFactory())
        source_controller = unreal.IKRigController.get_controller(rig)
        assert source_controller.set_skeletal_mesh(source_mesh)
        source_controller.apply_auto_generated_retarget_definition()
        component = unreal.new_object(unreal.SkeletalMeshComponent)
        component.set_skeletal_mesh_asset(source_mesh)
        source_bones = [str(component.get_bone_name(i)) for i in range(component.get_num_bones())]
        source_root = 'root' if 'root' in source_bones else source_bones[0]
        assert source_controller.set_root_motion_bone(source_root)
        if not source_controller.get_retarget_chains():
            # Epic's auto-characterizer does not recognize the colon-prefixed
            # Mixamo/HumanIK names retained by the source-only glTF transport.
            # Define source chains explicitly; never rename/rebase the paid target.
            import re
            def source_bone(*ends):
                for end in ends:
                    suffix = re.sub('[^a-z0-9]', '', end.lower())
                    matches = [bone for bone in source_bones if re.sub('[^a-z0-9]', '', bone.lower()).endswith(suffix)]
                    if len(matches) == 1:
                        return matches[0]
                raise ValueError(('Source bone missing/ambiguous', sid, ends, source_bones))
            assert source_controller.set_retarget_root(source_bone('hips', 'pelvis', 'hip'))
            definitions = [('Spine', ('spine', 'spine01', 'waist', 'abdomen', 'lowerback'), ('spine2', 'spine02', 'spine1', 'chest', 'upperback')),
                ('Neck', ('neck', 'neck1', 'necktwist01'), ('neck', 'neck1', 'necktwist01')), ('Head', ('head',), ('head',))]
            for side in ('Left', 'Right'):
                short = side[0].lower()
                definitions.extend([(side+'Clavicle', (side+'shoulder', short+'collar', short+'clavicle'), (side+'shoulder', short+'collar', short+'clavicle')),
                    (side+'Arm', (side+'arm', side+'upperarm', short+'shldr', short+'upperarm'), (side+'hand', short+'hand')),
                    (side+'Leg', (side+'upleg', side+'upperleg', short+'thigh'), (side+'foot', short+'foot'))])
                try:
                    toe = source_bone(side+'toebase', side+'toes', short+'toebase')
                    definitions.append((side+'Foot', (toe,), (toe,)))
                except ValueError:
                    pass  # Native CMU rigs have no toe joint; keep the target toe at its own rest.
            for chain_name, start, end in definitions:
                source_controller.add_retarget_chain(chain_name, source_bone(*start), source_bone(*end), 'None')
        assert len(source_controller.get_retarget_chains()) >= 8, ('No usable source characterization', sid)
        if sid == "cmu_bow":
            assert source_controller.set_retarget_root("Hips")
            assert source_controller.set_retarget_chain_start_bone("Spine", "LowerBack")
            assert source_controller.set_retarget_chain_end_bone("Neck", "Neck1")
        state.setdefault("source_chains", {})[sid] = chains(source_controller)
        retargeter = asset(package, "RTG_" + sid, unreal.IKRetargetFactory())
        controller = unreal.IKRetargeterController.get_controller(retargeter)
        controller.set_ik_rig(unreal.RetargetSourceOrTarget.SOURCE, rig)
        controller.set_ik_rig(unreal.RetargetSourceOrTarget.TARGET, target)
        controller.set_preview_mesh(unreal.RetargetSourceOrTarget.SOURCE, source_mesh)
        controller.set_preview_mesh(unreal.RetargetSourceOrTarget.TARGET, body)
        for mode in (unreal.RetargetSourceOrTarget.SOURCE, unreal.RetargetSourceOrTarget.TARGET):
            controller.reset_retarget_pose(controller.get_current_retarget_pose_name(mode), [], mode)
        if sid == "cmu_bow":
            # BVH's root offset is zero; its original calibration frame places the
            # pelvis one metre up. Correct the SOURCE retarget pose through Epic's
            # native control so root-height normalization has a valid baseline.
            # Neither source asset rests nor any target joints are rewritten.
            controller.set_root_offset_in_retarget_pose(unreal.Vector(0, 0, 100), unreal.RetargetSourceOrTarget.SOURCE)
        elif source.get('source_root_offset_cm') is not None:
            controller.set_root_offset_in_retarget_pose(unreal.Vector(0, 0, source['source_root_offset_cm']), unreal.RetargetSourceOrTarget.SOURCE)
        while controller.get_num_retarget_ops():
            controller.remove_retarget_op(controller.get_num_retarget_ops() - 1)
        controller.add_default_ops()
        for op in range(controller.get_num_retarget_ops()):
            controller.run_op_initial_setup(op)
        controller.auto_map_chains(unreal.AutoMapChainType.FUZZY, True)
        controller.auto_align_all_bones(unreal.RetargetSourceOrTarget.TARGET)
        held = [b for b in bones if any(digit in b for digit in ("Thumb", "Index", "Middle", "Ring", "Little"))]
        held += ["LeftFoot", "LeftToes", "RightFoot", "RightToes", "Jaw", "LeftEye", "RightEye"]
        controller.reset_retarget_pose(controller.get_current_retarget_pose_name(unreal.RetargetSourceOrTarget.TARGET),
                                       held, unreal.RetargetSourceOrTarget.TARGET)
        if sid == "fal_side_walk" or (sid == "fal" and target_id == "probe_ada"):
            # Acquired FAL WALK strafes face +X, while the source preview rest faces +Y.
            # Jog strafes/backward clips already match that rest and use no offset.
            # Epic's final additive pose op normalizes the entire output's facing;
            # no limb keys, original assets or native skeleton rests are changed.
            mode = unreal.RetargetSourceOrTarget.TARGET
            base_pose = controller.get_current_retarget_pose_name(mode)
            facing_pose = controller.create_retarget_pose("FAL_SourceFacing", mode)
            controller.set_current_retarget_pose(facing_pose, mode)
            controller.reset_retarget_pose(facing_pose, [], mode)
            reference_pose = unreal.AnimPoseExtensions.get_reference_pose(body.get_editor_property("skeleton"))
            reference = unreal.AnimPoseExtensions.get_ref_bone_pose(reference_pose, "Root", unreal.AnimPoseSpaces.WORLD).rotation
            inverse = unreal.Quat(x=-reference.x, y=-reference.y, z=-reference.z, w=reference.w)
            delta = inverse * unreal.Rotator(pitch=0, yaw=90, roll=0).quaternion() * reference
            state['facing_normalization'] = {'root_reference': str(reference), 'local_delta': str(delta), 'global_yaw_degrees': 90}
            controller.set_rotation_offset_for_retarget_pose_bone("Root", delta, mode)
            controller.set_current_retarget_pose(base_pose, mode)
            index = controller.add_retarget_op("/Script/IKRig.IKRetargetAdditivePoseOp")
            assert index >= 0
            op = controller.get_op_controller(index)
            settings = op.get_settings()
            settings.pose_to_apply = facing_pose
            settings.alpha = 1
            op.set_settings(settings)
        candidates = [(a["name"], unreal.load_asset(a["asset"])) for a in source["assets"] if a["kind"] == "animation"]
        prefix = Path(source["path"]).stem
        chosen = {}
        for key, name in todo.items():
            matches = [animation for candidate, animation in candidates if animation.get_path_name() == name
                       or candidate in (name, prefix + name, prefix + "_" + name)]
            assert len(matches) == 1, (sid, key, [candidate for candidate, _ in candidates])
            chosen[key] = matches[0]
        inputs = unreal.IKRetargetBatchOperationInputs()
        inputs.assets_to_retarget = [unreal.EditorAssetLibrary.find_asset_data(a.get_path_name()) for a in dict.fromkeys(chosen.values())]
        inputs.source_mesh = source_mesh
        inputs.target_mesh = body
        inputs.ik_retarget_asset = retargeter
        inputs.target_path = package + "/Clips/" + sid
        inputs.prefix = "HG_" + target_id + "_"
        inputs.include_referenced_assets = False
        inputs.overwrite_existing_files = True
        save("RETARGETING", current=sid)
        results = [r.get_asset() for r in unreal.IKRetargetBatchOperation.run_batch_retarget(inputs)]
        assert len(results) == len(inputs.assets_to_retarget)
        for key, original in chosen.items():
            animation = next(a for a in results if a.get_name() == inputs.prefix + original.get_name())
            animation.set_preview_skeletal_mesh(body)
            options = unreal.GLTFExportOptions()
            options.bake_material_inputs = unreal.GLTFMaterialBakeMode.DISABLED
            options.export_vertex_skin_weights = True
            options.export_morph_targets = False
            options.export_animation_sequences = True
            options.export_preview_mesh = True
            options.export_uniform_scale = .01
            options.default_level_of_detail = 0
            options.use_mesh_quantization = False
            folder = root / "probe_clips" if target_id == "probe_ada" else root / "clips" / target_id
            path = folder / (key + ".glb")
            path.parent.mkdir(exist_ok=True, parents=True)
            task = unreal.AssetExportTask()
            task.object = animation
            task.filename = str(path)
            task.automated = True
            task.prompt = False
            task.replace_identical = True
            task.options = options
            assert unreal.Exporter.run_asset_export_task(task)
            if sid == "fal_spear" or (sid == "fal" and target_id == "probe_ada"):
                original.set_preview_skeletal_mesh(source_mesh)
                task.object = original
                task.filename = str(folder / (key + "_source.glb"))
                assert unreal.Exporter.run_asset_export_task(task)
            data = path.read_bytes()
            gltf = json.loads(data[20:20 + struct.unpack_from("<I", data, 12)[0]])
            assert len(gltf["skins"][0]["joints"]) == 59 and gltf.get("animations")
            state["clips"].append({"state": key, "source": sid + "/" + todo[key], "source_asset": original.get_path_name(),
                                   "asset": animation.get_path_name(), "path": str(path), "seconds": animation.sequence_length,
                                   "loop": original.get_name().lower().endswith("_loop") or key.startswith("work_") or key in ("eat", "drink", "sleep", "idle", "idle_look", "walk", "run", "sprint", "talk", "talk_1", "talk_2", "talk_3", "sit", "sit_talk", "fall", "sword_ready", "spear_ready", "bow_ready", "crouch_idle", "crouch_walk", "captive_kneel"),
                                   "source_rate_scale": original.get_editor_property("rate_scale")})
            save("EXPORTED", current=key)
    assert len(state["clips"]) == len(selected), "Not every selected source was exported"
    unreal.EditorAssetLibrary.save_directory(package, only_if_is_dirty=True, recursive=True)
    save("DONE", minutes=(time.monotonic() - started) / 60)
except Exception:
    save("FAILED", error=traceback.format_exc())
if os.environ.get("OTHERREACH_HUMGEN_KEEP_EDITOR") != "1":
    unreal.SystemLibrary.quit_editor()
