# Native MetaHuman playtest pipeline

Branch `playtest/mh-characters` starts at frozen RC d `4c6bbe11` and includes the approved slice `59aa190e`. Licensed/generated assets stay outside Git. The frozen package is read-only.

The runtime bundle is selected by `OTHERREACH_MH_CHARACTERS_ROOT`, or `mh_characters` next to the executable. `catalog.json` names separate native body/head GLBs, native DCC map folders, native eye/groom manifests, outfit configuration and one baked Hidden Face Map per character/outfit combination. Players remain at full native LOD; NPC LOD selection must be recorded by the native export job and the GLB audit. Missing required meshes, textures, masks or named garment binds fail the character load.

All UE jobs use the original slice project and real RHI under `C:/Otherreach_Recovery/gpu_lock.ps1`, with owner-playing cancellation and a 60-second cooldown after release. Run small batches; do not hold the GPU while queued work waits. Never use NullRHI for these jobs.

1. `probe_npc_sources.py`: read native presets and LOD counts.
2. `prepare_npcs.py`: duplicate ten fresh Epic presets into `/Game/MHPlaytest/NPC`, set native Body Params and 4K source resolutions, and choose native grooms. Age descriptions are selection intent until visual review.
3. `cloud_export_npcs.py`: native Full Rig, 4K source request, separate head/body plus Full Body utility, DCC/DNA and Materials Export. Environment `OTHERREACH_MH_NPC_START` and `OTHERREACH_MH_NPC_COUNT` select a small checkpointed batch.
4. `assemble_npcs.py`: native UE Optimized assembly for semantic face materials and conformed groom cards. Export native lower LODs; do not decimate or prune joints. `audit_glb.py` distinguishes the complete exported reference skeleton from actually weighted joints and counts all influence sets.
5. Stage the runtime catalog and native exports. Run `res://MHCharacters/Tests/verify_native_character.gd` with `OTHERREACH_MH_BAKE_MASKS=1` once to classify covered triangle UVs. `raster_hidden_face_maps.py` creates 2048px mask textures. Body UVs use a repeating native tile; the shader samples the mask with `fract(UV)` without changing mesh UVs.
6. Run the same regression with baking disabled. It checks exact native body mesh/array preservation, garment named binds and LeaderPose. No body ArrayMesh rebuild, rig merge, rebind, rest fixer or body modifier occurs here.

The NPC source/export jobs are preparations, not acceptance. Complete the normal-game creator, NPC and animation checks; measure at the owner's settings; export a committed revision only with `tools/export_playtest_clean.ps1`; launch the package via its actual desktop launcher and capture G4 evidence. The owner approves the result.

The wardrobe plan accepts DevonLux shirt/vest/trousers/hood, with tint and part variants. Rejected boots/gloves are excluded. Claude's body-shell footwear is a separate handoff; bare hands/feet are allowed until it is accepted. The reviewed DeepSeek AnimationLibrary is another handoff. Until it arrives the figure holds its imported base pose and reports idle pending; it does not synthesize locomotion.
