# Canonical MetaHuman pipeline tools

Shared versioned scripts: tools/metahuman on playtest/mh-rcd1-owner, worktree C:\Otherreach_Build\playtest_mh_rcd1_owner_wt. Runtime integration remains src/Presentation/MHCharacters. Coordination runbook: C:\Otherreach_External_Assets\MH_PIPELINE_RUNBOOK.md. DeepSeek's armory importer must call these scripts instead of private copies. MIGRATION.json records former paths.

The owner package remains built from e51f50fb. This tools-only consolidation does not alter it. Claude accepted its final geometry and dark wool. The normal 15-minute feel pass belongs to the owner and has not been completed by automation. Performance work is stopped.

## Setup and native character export

Set OTHERREACH_PIPELINE_ROOT to a fresh absolute job directory containing runtime/catalog.json, evidence and required source manifests. Never target the frozen D:\Otherreach_Scratch\mh_playtest_20261019 tree or a live owner package. Python root validation rejects frozen descendants too. Licensed assets stay outside Git; this is a staged pipeline, not an asset downloader. Installed prerequisites: UE5.8 with Creator/GLTFExporter, Blender5.2, Godot4.7.2 C#, Python/Pillow. Wrappers record their installed paths.

probe_npc_sources.py -> prepare_npcs.py -> cloud_export_npcs.py -> assemble_npcs.py -> export_npc_glb.py/export_npc_body_lod0.py -> clean_npc_gltf.py/clean_npc_body_lod0.py -> staging. Record presets, native Body Params, full rig, 4K sources and assembly/LOD settings. Body/head/material/groom exports require real commandlet rendering. Players retain full accepted native meshes; NPCs use native BodyLOD0 and Optimized High face. Tested lower body/face candidates failed the neck seam.

Keep body342/head875 separate and untouched, with their own native skins. Rigged garments use named binds. Copy32 shared head bones from the body global pose after animation (LeaderPose); runtime body IK stays off. Never rebuild native body ArrayMesh buffers or alter shape, weights, hierarchy, rest or inverse binds. Face roles use the ibrews/MetaHumanGodot release.gd surface-index table: skin, teeth, hide, eyeR, eyeL, hide, hide, skin, lashes. Exported names are unreliable.

## Animation

prepare_ue_animation_sources.py imports original Quaternius/KayKit clips. export_native_animation_roster.ps1 calls the single retarget_native_animations.py implementation: Epic IK Retargeter and native GLTFExporter. Animation-only exports use the proven NullRHI commandlet route; their preview mesh is never staged as the body. No Godot retarget and no Mixamo.

Duplicate IK rigs; pelvis stays retarget root, root-motion bone is root. Exclude all18 finger/metacarpal chains. After auto_align_all_bones reset fingers and foot_l/ball_l/foot_r/ball_r retarget-pose offsets, avoiding the roughly67-degree foot twist. Native skeleton/rest remain unchanged. UE retarget assets are isolated per job-root name.

stage_native_animation_roster.ps1 calls clean_native_animations.py (Blender bridge retaining342 joints), then build_native_animation_library.gd (exact names/parents/rest audit, track-path rebinding only). Manifest: ue_native_animations_extremities.json; bridge reports: clean_extremities_<id>.json. Legacy rootfix/relaxed/extremities entrypoints delegate to these implementations. verify_native_extremities.gd checks finger rest and foot direction; old-library negative test failed8 feet, corrected libraries passed24.

## Garments and hidden masks

rebind_piece_direction.py takes source.glb native_body.glb output.glb after Blender --python ... --. Only garments change: hierarchy-ordered head positioning plus minimal bone-to-child direction alignment, roll preserved, terminal translation only. Bake garment deformation, retain author weights, attach to exact native target armature, export garment only. Never copy full world bone rotation. DeepSeek owns armory integration.

Show native hands; use real direction-rebound DevonLux leather boots. Body-shell gloves/boots are rejected; former builders stop explicitly. build_shells.py permits trousers only. build_native_trousers.py -> rebind_hose_all.ps1 -> stage_direction_hose.py produces per-body wool hose with all influences/PBR detail. WOOL_PALETTE.json carries approved colours; multiply offline in linear light and encode sRGB. Player chest retains base DevonLux colours.

bake_posed_hidden_maps.gd -> raster_hidden_face_maps.py makes Body Hidden Face Maps. native_wrist_uv.gd/native_covered_legs_uv.gd -> raster_native_hands_masks.py supplements the full-length hose/tall-boot mask: covered skin below pelvis-rest+12cm, native hands/fingers and8cm wrist radius restored last. This coverage rule is outfit-specific, unsuitable for bare legs. Rebake after outfit changes; never delete body triangles. Shader uses fract(UV).

## Gates and packaging

G1: rendered head/body boundary <0.1mm under LeaderPose. G2: unchanged native body buffers/proportions/skeleton/rest/binds. G3: exact source clip names at identical times with both animators paused; inspect feet, hips, shoulders and hands. G4: real packaged desktop launcher, creator, spawn, grass/art, clothed animated NPCs, hover rim, key pickup, small loot window, loading and necks. Headless checks do not replace rendered proof. Claude/owner judge frames; capture completion is not acceptance.

capture_animation_gate.gd and run_full_animation_gate.ps1 capture comparisons. Launch run_dressed_release_gates.ps1 through run_guarded.ps1 -Gpu; it assumes that external lease. Do not nest guards around wrappers already acquiring GPU. run_guarded.ps1 uses C:\Otherreach_Recovery\gpu_lock.ps1, aborts for OWNER_PLAYING, stops only its owned tree and propagates failures. Observe61-second fairness after release. Never use GPU while owner plays.

Export committed source with export_playtest.ps1 through run_guarded.ps1 -Gpu. It calls the approved clean exporter with MSBUILDDISABLENODEREUSE=1 and DOTNET_CLI_USE_MSBUILD_SERVER=0. Include invoke_gpu_lock.ps1 beside the launcher. Direct no-args EXE uses PLAY defaults. Preserve f87 backup and art junction anchors. Keep DevonLux CC-BY and wool/source-animation CC0 credits.

Normal gameplay including --autostart uses owner machine.FrameCap. Only deterministic playthrough/delta-shots/--shot/--survey branches cap to simulation tick; leave them intact. No performance jobs now.

CPU validation: python test_pipeline_paths.py; Python compilation; PowerShell Parser.ParseFile; Godot --headless --check-only --script against an imported project. These checks do not rerun the asset pipeline.

## Equipped sword grip (2026-10-20)

The312 locomotion/combat clips keep both hands at native relaxed rest. For a weapon hand, set OTHERREACH_MH_HAND_GRIP=1 and OTHERREACH_MH_ANIM_CHARACTERS to selected native ids when running retarget_native_animations.py: a separate Epic-retargeted Sword_Idle export retains five right-digit chains, excludes the left digits and metacarpals, and writes evidence/ue_native_weapon_grips.json. Run clean_native_animations.py with OTHERREACH_MH_ANIM_MANIFEST=ue_native_weapon_grips.json, then extract_native_weapon_grips.gd. The extractor verifies every342-bone parent/rest against the selected accepted body and saves only15 right-digit rotations, a native-body SHA256 and provenance to runtime/hand_grips/<id>.json. It never writes the accepted animation libraries. Runtime WeaponGrip.gd uses Godot SkeletonModifier3D to blend this authored pose after animation while a right-hand sword is equipped; unequip releases it. Left hand and native rest/binds/buffers remain untouched. Review actual HeldWeapon socket views, not just an empty fist.

For a fresh runtime overlay, place writable outfit masks in an owned physical directory and update hidden_mask in its copied catalog. Mask UV/raster summaries now write to the job's evidence directory, not a potentially shared runtime/evidence directory. Never write through junctions to the delivered runtime.
