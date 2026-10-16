UNREAL CONTENT PACKS -> OTHERREACH ASSET SOURCES  (tools\asset_pipeline\ue_export)
================================================================================
BEAST, UE 5.8.3, Blender 5.2.2, Godot 4.7.2. Written and run 2026-10-14/15.

Files
  export_pack.py      Unreal Editor Python. Every StaticMesh/SkeletalMesh under /Game/<Pack> (optionally --match regex)
                      -> Meshes\<key>.fbx (LOD0, no collision; Nanite = full source mesh), Textures\<T_name>.png|.exr
                      (source art, full resolution, each texture once per pack), Manifests\<key>.json, _pack.json.
                      --textures-only (VFX packs): every Texture2D under the pack -> Textures\, and in _pack.json each
                      material's blend mode, shading model, scalar/vector parameters and textures; no meshes.
                      --animations: every AnimSequence -> Animations\<key>.fbx + Manifests\<key>.json (section 9).
  export_pack.ps1     wrapper: export_pack.ps1 <uproject> <Pack> <outdir> [-Source fab|cosmos] [-Match regex] [-SkipExisting]
                      [-TexturesOnly] [-Animations]
  convert_ue.py       Blender: convert_asset.py driven by a manifest (--manifest) instead of FBX materials.
  thumbs_blender.py   Blender: one base-colour thumbnail per manifest -> <pack>\_thumbs\
  inventory.py        name-based kit categories + labelled contact sheets + INVENTORY.txt
  anim_fbx_to_glb.py  Blender: export_pack.py --animations FBX -> <pack>_anim\Glb\<key>.glb, the clip on its UE skeleton (section 9)


1. HOLDING PROJECT
------------------
Packs are exported from C:\Otherreach_External_Assets\Fab\ExportHost\ExportHost.uproject, never from FabAssets
(the owner's editor stays open on FabAssets; a second instance writing its Saved\/DDC would collide).
ExportHost enables PythonScriptPlugin + EditorScriptingUtilities and disables Fab. Copy a pack in with
  robocopy C:\Otherreach_External_Assets\Fab\FabAssets\Content\<Pack> C:\Otherreach_External_Assets\Fab\ExportHost\Content\<Pack> /E /MT:16
Cosmos "Purchased\Environments\*.zip" are whole UE projects (Content\<Pack>\ + __ExternalActors__ + Config). Extract
only Content\<Pack>\ into ExportHost\Content\<Pack>\ (5 s each; Frozen Norse 1.55 GB, Japanese Village 2.13 GB). UE 5.0-5.5
packages load in 5.8 without resaving. Check cross-folder references first (strings in the .uasset):
FantasyCastle/LostTemple/Mayan/Japanese_Village are self-contained; Frozen Norse also references /Game/VFX (not shipped,
only affects VFX materials). Current ExportHost size 12 GB (FantasyCastle, LostTemple, Ancient_Mayan_Ruins,
FrozenNorseEnvironment, Japanese_Village); delete a pack folder there once exported.


2. COMMANDS (exact, as run)
---------------------------
  set T=C:\Otherreach_Recovery\UNNAMED\tools\asset_pipeline\ue_export
  set U=C:\Otherreach_External_Assets\Fab\ExportHost\ExportHost.uproject
  powershell -ExecutionPolicy Bypass -File %T%\export_pack.ps1 %U% FantasyCastle C:\Otherreach_External_Assets\UE_Exports\FantasyCastle
  (same for LostTemple, Ancient_Mayan_Ruins)
  set M=wall^|roof^|door^|gate^|window^|floor^|stair^|step^|pillar^|column^|arch^|beam^|foundation^|extension^|fence^|tower^|house^|build^|accom^|castle^|temple^|bridge^|trim^|module^|kit^|tile^|capital^|pedestal^|handrail^|rail^|ceil^|wood_0^|thatch^|trench^|plinth^|platform^|balcon^|balkony^|ruin
  powershell -ExecutionPolicy Bypass -File %T%\export_pack.ps1 %U% FrozenNorseEnvironment C:\Otherreach_External_Assets\UE_Exports\FrozenNorseEnvironment -Source cosmos -Match "%M%"
  powershell -ExecutionPolicy Bypass -File %T%\export_pack.ps1 %U% Japanese_Village C:\Otherreach_External_Assets\UE_Exports\Japanese_Village -Source cosmos -Match "%M%"
Use -Match (building/modular regex above) for new packs: disk on C: is tight. -SkipExisting reuses the FBX and textures on
disk and only rewrites manifests (15-60 s a pack) - use it after changing the channel rules.
VFX packs (2026-10-15, 14 packs, 5-70 s each): textures and material records only -
  powershell -ExecutionPolicy Bypass -File %T%\export_pack.ps1 %U% FireFX_VDB C:\Otherreach_External_Assets\UE_Exports\FireFX_VDB -TexturesOnly
Raw form (what the .ps1 runs):
  UnrealEditor-Cmd.exe C:/.../ExportHost.uproject -run=pythonscript "-script=C:/.../export_pack.py --pack <Pack> --out C:/.../<Pack> --source fab" -unattended -nop4 -nosplash -NullRHI -stdout -FullStdOutLogOutput

  set B="C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
  %B% --background --factory-startup --python %T%\thumbs_blender.py -- C:\Otherreach_External_Assets\UE_Exports\<Pack>
  python %T%\inventory.py C:\Otherreach_External_Assets\UE_Exports\<PackA> [<PackB> ...] --sheets C:\Otherreach_External_Assets\UE_Exports\_sheets
  %B% --background --factory-startup --python-exit-code 1 --python %T%\convert_ue.py -- ^
      --manifest C:\Otherreach_External_Assets\UE_Exports\FantasyCastle\Manifests\SM_House.json ^
      --asset-id bld_fab_fc_house_01 --res 2048 --category structure --preview ^
      --out C:\Otherreach_Recovery\asset_work\ue_export\ready --work C:\Otherreach_Recovery\asset_work\ue_export\_work
  python ..\validate_godot.py --all C:\Otherreach_Recovery\asset_work\ue_export\ready --probe C:\Otherreach_Recovery\asset_work\ue_export\_godot_probe
(own --probe dir: other agents use the default probe project.) All convert_asset.py options pass through
(--max-tris, --lods, --res, --licence ...); --source defaults to the manifest's FBX, --licence to the manifest's note.


3. TIMINGS AND SIZES (BEAST)
----------------------------
  pack                     meshes  textures  full export  Meshes   Textures  thumbs
  FantasyCastle (Fab)        121      289       424 s      416 MB   1.8 GB     51 s
  LostTemple (Fab)           153      370       269 s      117 MB   1.7 GB     40 s
  Ancient_Mayan_Ruins (Fab)  256      454       484 s      388 MB   2.9 GB     95 s
  FrozenNorseEnvironment      44*      42        87 s       14 MB   500 MB     ~15 s
  Japanese_Village           228*      43       209 s      132 MB   500 MB     ~60 s
  * building/modular -Match subset (Frozen Norse has 102 static meshes, Japanese Village 409).
UE_Exports total 8.4 GB; the three Fab packs were exported in full (all meshes) before the space limit - their foliage,
rock and prop FBX/textures can be deleted if space is needed. Editor start-up ~10 s; FBX export dominates.
convert_ue.py: 5-18 s per mesh; validate_godot: ~3 s for 8 assets.


4. MANIFEST (Manifests\<key>.json)
----------------------------------
  pack, mesh (/Game path), class, fbx, units ("centimetres (Unreal), Z up"), licence, key
  lod_count, nanite, triangles_lod0 (source MeshDescription), triangles_render_lod0 (render/Nanite fallback),
  nanite_fallback_percent, slots, bounds_cm {min, max, size}
  materials[slot] = {slot_material, base_material, blend_mode, scalars, vectors,
                     textures [{param, texture, channel, file, source_px, normal_convention}],   <- every map the material reads
                     channels {basecolor, normal{convention}, orm{packing}, ao, roughness, metallic, opacity, emissive,
                               height, mask: {file, texture}}}                                   <- the one picked per channel
  (key = asset name, prefixed with its sub-folder when it is not Mesh/Meshs/Meshes, e.g. Mesh__Foliage__SM_foliage01)


5. GOTCHAS
----------
UE 5.8 Python
  * -script="..." is unescaped by the commandlet: backslashes become \t, \u ... Pass every path with forward slashes.
  * Arguments after the script path in -script reach sys.argv (argparse works).
  * AssetRegistry.get_asset_by_object_path(str) is deprecated and will not take a SoftObjectPath in 5.8; use
    EditorAssetLibrary.find_asset_data(path). ARFilter(class_paths=...) returned nothing; get_assets_by_path(path, recursive=True)
    filtered by asset_class_path.asset_name works.
  * MaterialEditingLibrary.get_used_textures is deprecated; get_material_used_textures(mi) works on instances.
    Texture params: get_texture_parameter_names(base) + get_material_instance_texture_parameter_value(mi, name) (parent
    defaults included).
  * Export: unreal.AssetExportTask + Exporter.run_asset_export_task; StaticMeshExporterFBX with FbxExportOption
    (level_of_detail False, collision False, export_source_mesh True, fbx_export_compatibility FBX_2020).
Textures
  * TextureExporterPNG/TGA ASSERT (kills the whole commandlet, no Python exception) on a source format they cannot write.
    Route by the asset-registry tag SourceFormat: G8/BGRA8/G16/RGBA16 -> PNG, RGBA16F/RGBA32F/R16F/R32F -> EXR, BGRE8 -> HDR.
    FantasyCastle has RGBA16F base colours/normals (EXR, ~17-20 MB each).
  * Exporters write the source art (tag Dimensions), not the cooked/LOD-biased size (cooked_px in _pack.json).
  * Normals are exported as authored. convention "DirectX (-Y)" unless the texture had Flip Green Channel set
    (Mayan normals are OpenGL); convert_ue.py flips green only for DirectX.
  * Packed maps, measured by channel means: _ORM (FantasyCastle, Mayan, Japanese, Frozen Norse) R=AO G=rough B=metal;
    _RMA / _RMAH (LostTemple, Mayan) R=rough G=metal B=AO; _ORDp (FantasyCastle terrain) B=displacement, no metal.
    manifest orm.packing carries it; convert_ue.py maps it.
  * Japanese_Village _B maps carry alpha = 0 everywhere (not opacity). Opacity is only taken for MASKED/TRANSLUCENT
    materials; viewers that honour base-colour alpha show the mesh invisible.
  * Mayan _BC_H = base colour with height in alpha.
  * VictorianGrandMansion _MRA: R = metal (~0), G = rough, B = AO (packing "MRA").
  * Plain Materials with hard-wired Texture Samples (VictorianGrandMansion: no instances, no texture parameters) report no
    used textures under -NullRHI; export_pack.py then takes the Texture2D packages the material depends on (2026-10-16).
  * Every Fab pack checked stores texture source as Oodle FCompressedBuffer (FantasyCastle, LAYA city/sci-fi packs ...).
    UE decodes it; only a pure-Python .uasset reader cannot (the ASTRAL "Oodle" failure). Export through UE.
Materials (what is NOT reproduced)
  * These packs use master materials that blend layers: FantasyCastle M_Rock*/M_Rock22 (3 texture sets by vertex/mask + snow +
    moss), LostTemple M_Master02 (dirt overlay, floor trash, albedo tint 1.5), Mayan wall (base + top + moss), Frozen Norse
    kits (unique baked normal + unique ORM, colour from 3 tiling detail textures through an RGB mask with tints).
    The manifest picks one primary set (base colour + same-named normal/ORM; normal prefers the ORM's name); tints, tiling
    scalars, snow/moss layers are only recorded. Frozen Norse looks right in tone but its colour is a tiling detail
    texture, not the authored blend - a mask-composite bake is the follow-up for hero use.
  * Material overrides on placed actors in the .umap are not captured. Mayan has 24 meshes with empty slots or only
    placeholder textures (pyramid_A, top_module_*, brick_trim_*, stone_door_staute*, xblock_*); FantasyCastle
    SM_CastleWallV1UvStack (and *Uvd variants) carry T_Defaul* placeholders and some slots on WorldGridMaterial
    (converted flat grey, UE_WARN in the log). Check the sheets before picking.
Geometry
  * Nanite: FBX export with export_source_mesh=True writes the full source mesh (FantasyCastle SM_AccomsP12 11,955 tris vs
    2,266 render fallback; SM_CastleUvd 913k; Mayan pyramid_D 854k). Always pass --max-tris for those. The fallback is
    not what you want: it is a decimated proxy.
  * Japanese_Village FBX import with a 0.01 object scale; Blender's matrix_world is stale until view_layer.update().
  * convert_asset.py decimates LOD0 (--max-tris) after it origins the mesh, which can leave the base a few mm above Y=0
    (validate_godot FAIL seen on a 308k->40k wall). convert_ue.py re-grounds LOD0 after that decimation. Collapse-decimated
    LOD2 can drift > 1 % off centre on spiky silhouettes (castle keep): use --lods 0.5,0.3.
  * Scale is pack-native cm. Mayan wall kit pieces are small (SM_Castle_Wall_01 0.6x2.1x0.7 m) while its big walls are
    50 m+ - they are placed scaled in the scene; check footprint against the census before binding.
  * Output .log from the .ps1 is UTF-16 (PowerShell 5 redirection); Select-String reads it, grep does not.
Licence (blocking for promotion)
  * Fab: many listings migrated from the UE Marketplace are "UE-Only Content" - those may not ship in a Godot game.
    Every manifest/meta says UNVERIFIED; check each Fab listing (FantasyCastle, LostTemple, Ancient Mayan Ruins) before use.
  * Cosmos purchased environments: no licence text in the zip; UNVERIFIED until filled from the Cosmos terms.


6. CONVERTED SAMPLES (C:\Otherreach_Recovery\asset_work\ue_export\ready, all PASS validate_godot, previews in each folder
   and together in UE_Exports\_sheets\_converted_previews.png)
  id                            source                                     LOD0 / lod1 / lod2 tris   size m (x,y,z glTF)
  bld_fab_fc_house_01           FantasyCastle SM_House                     8108 / 4054 / 1615        10.5 x 9.3 x 6.4
  bld_fab_fc_tower_small_01     FantasyCastle SM_TowerSmall                1406 / 703 / 281          6.9 x 21.6 x 6.3
  bld_fab_fc_castle_keep_01     FantasyCastle SM_CastleSep3 (--lods 0.5,0.3) 15119 / 7559 / 4535    11.9 x 18.3 x 11.6
  wall_fab_lt_temple_wall_01    LostTemple SM_wall01                       9992 / 4995 / 1998        4.2 x 6.3 x 0.8
  bld_fab_amr_archi_module_02   Ancient_Mayan_Ruins SM_archi_module_002    4710 / 2354 / 941         9.0 x 10.9 x 5.5
  wall_cos_fn_wall_module_01    FrozenNorse SM_Wall_Module                 29350 / 14674 / 5870      6.0 x 3.0 x 1.2
  roof_cos_jv_roof_05           Japanese_Village SM_Roof_05                24660 / 12330 / 4932      7.9 x 4.2 x 9.0
  wall_cos_jv_wall_2x3_05       Japanese_Village SM_Wall2x3_05             2602 / 1300 / 520         2.0 x 3.0 x 0.1
FantasyCastle buildings are stylised-leaning (saturated blue roofs, clean plaster) - judge against the no-stylised rule.


7. INVENTORY (full lists with sizes: UE_Exports\_sheets\INVENTORY.txt; sheets UE_Exports\_sheets\<Pack>_<category>[_n].png)
Categories are by name only.
  FantasyCastle (121): 31 whole buildings/towers (SM_House/3/4/6, HouseAdd1/2, Accoms P5-P16, Build1-6, CastleSep1-5,
    TowerSmall, TowerT3, TowerrType2, FarTower, TempleDoor), castle/bridge megastructures (CastleUvd 193 m, Bridge*,
    Platform, AlchYard), 18 wall/rail pieces (InsideWall, Rails*), Balkony3; no separate roof/door/window kit - houses
    are single meshes with roofs, doors, windows modelled in. Plus rocks, mountains, foliage, knights.
  LostTemple (153): interior temple kit - 25 walls/fences (wall01/02/03, middlewall*, ceilingwall*, smallfence*,
    fence01/02), 49 structure pieces (floor*, ceiling*, ceilingdeck*, pillars, stair/understair, updan* trims), ornate
    door-arch wall (wall01), braziers, pots, statues; 60 foliage cards. No exterior houses or roofs.
  Ancient_Mayan_Ruins (256): 11 landmarks (pyramids A-D 45-93 m, mayan_tower 50 m, pyramid modules), 40 walls
    (Castle_Wall_01-04, Mossy_Stone_Wall, Wall_A/B, xblock walls), 57 structure (archi_* arch modules, brick/top/trim
    modules, pillars), 20 stone door modules, ruin roof; cave cliffs; 96 props incl. modern clutter (cables, pens,
    tower light) - keep those out of the start region.
  FrozenNorseEnvironment (44 exported of 102): Norse stone ruin kit - Arch_Module + Arch_kit_01-04, Trim_Arch_Module,
    Wall_Module, Gate (35 m), Giant_Column, Column_Module/Piece_01-04, Capitals, Pedestal, Bridge, Handrails,
    StairsKit_01-03 + Stairs_01-10 stones, Tile/Tilekit floor slabs. No houses or roofs (glaciers, cliffs, runestones,
    throne, statue not exported).
  Japanese_Village (228 exported of 409): full timber house kit - 23 roofs (Roof_01-05, Roof01/02_Thatch, RoofThatch_05,
    RoofCap_01/02, WoodRoof shingle tiles 001-007, Extension thatch), 41 walls/fences (Wall1x1 ... Wall7_5x1, Fence_01-12,
    TempleFence, Rockwall*/Trench*), 74 structure (Foundation*, FoundationCorner*, WoodFloor 000-027, Beam_01-24,
    Stair_01-03, Temple_Stair, Arch_01/02, Bridge_03-06), Tower 14 m, 84 construction planks (Wood_008-083). Whole houses
    exist only as Blueprints/PackedLevelActors (BP_L_House_01, BP_M_House_01, BP_FishHut_01-03) assembled from these
    pieces - not exported (would need actor-transform baking).
Not done: Dwarven Citadel, Asian Spirit Tree Temple (zips present, not extracted); Medieval Lake/Fantasy Village and Old
Windmills Fab packs not yet in FabAssets.


8. VFX PACKS (2026-10-15, export_pack.py --textures-only; asset_work\vfx_fab\DECISIONS.txt has the choices)
--------------------------------------------------------------------------------------------------------
What ports to Godot: Texture2D flipbooks, sprite sheets, glows and noise. Niagara / Niagara Fluids systems (and their
simulations), Alembic caches and Sparse Volume Textures (SVT_*, the "VDB" packs) do not; an SVT ports only as a render.
  FireFX_VDB      13 fire Blueprints over SVTs (126 frames, 30 fps, ~1 cm voxels) + T_BrightCenterEnergy (2x2 glow)
  CauldronFX_VDB  5 coloured (magic) cauldron smokes as SVTs, additive; cauldron mesh textures
  AmtosphericFX   T_Fire / T_Smoke / T_SmokeWispy (4x4 grey sprite variants, not animations), T_Ember, T_Particulate,
                  T_HeatHaze_Normal; the rest is a modern street demo (buildings, dumpster, bollard)
  NatureWaterFX   T_Smoke_01 / T_SmokeWispy (+ normals), T_Smoke3, geyser splash, wake (4x4 variants)
  ParticleRings   3x3 4096 sheets: DenseSmoke, PuffySmoke, Mist, GulfMist, MistWave, SmokeWave, Spice (ring sprites)
  ShockWaves      smoke / thick smoke / splatter 3x3 sheets, circles, noise
  LeavesNiagaraFX 16 single leaves (+ normals), grass strand
  FestivalFX      fine / rich smoke (+ normal), spark, snowflake, confetti
  DashFX, LevitationFX, AbsorbtionFX, EtherealTentacleFX  stylised energy cores/ribbons/tentacles/caustics + UE mannequin
                  demo textures; Shockwave_NiagaraFluids, ShockwavesVDB: demo ground textures only
Same-named textures in two folders of one pack export to one file - the first one found.
SVT -> flipbook: bake_vdb.py renders each frame of a FireFX Blueprint's volume in the FULL editor on
ExportHost (orthographic SceneCapture2D, SceneColorHDR, black level, EXR per frame; -ExecutePythonScript with
EditorPythonScripting.set_keep_python_script_alive so the editor keeps ticking; GPU job - take gpu.lock), and
compose_flipbook.py crops, loops (cross-fade) and packs the frames into an 8x8 atlas (asset_work\vfx_fab\_scripts has the
runners: run_bake.ps1 under gpu_batch.ps1, make_fire.ps1). 5 fires x 126 frames at 2048 px: 3.5 min plus ~20 s start;
EXR frames ~4 MB each (delete after composing). Gotchas met on the way:
  * -ExecutePythonScript closes the editor as soon as the script returns: a tick-driven script must call
    unreal.EditorPythonScripting.set_keep_python_script_alive(True), and False when it is done.
  * In UE 5.8 EDITOR builds a HeterogeneousVolumeComponent cannot be stepped with SetFrame: its tick puts the requested
    frame object into its dynamic material, and next tick its WITH_EDITOR "material changed" check reads that 1-frame
    texture and pins Start/End/Frame to 0. bake_vdb.py puts the animated texture back after each capture (unpin) on a
    plain Movable HeterogeneousVolume actor carrying the Blueprint's material (no construction script to re-run).
  * A newly set frame streams in a few hundred ms later; until then the old one draws. A 64 px probe capture with the
    same framing is compared tick to tick (exact equality, jitter off) before the full-size capture is exported.
  * r.HeterogeneousVolumes.Jitter 0 and .StochasticFiltering 0: a single capture otherwise keeps the ray-march noise
    (a main view's TAA averages it away) and no two captures of one frame are equal.
  * The Blueprints' Niagara NS_Embers (own clock) are left out with the Blueprint; the game draws its own embers.
  * The ExportHost RHI is D3D12 SM5: the first run compiles the heterogeneous-volume shaders (~1 min; FXC falls back to
    intermediate HLSL - harmless). UE writes EXR (ZIP, half); this OpenCV has no EXR codec - compose_flipbook.read_exr.


9. ANIMATION PACKS (2026-10-16; export_pack.py --animations, used by tools\asset_pipeline\locomotion_fab.py)
-----------------------------------------------------------------------------------------------------------
  powershell -ExecutionPolicy Bypass -File %T%\export_pack.ps1 %U% SampleAnimationPack D:\Otherreach_Depot\UE_Exports\SampleAnimationPack_anim -Animations
  %B% --background --factory-startup --python %T%\anim_fbx_to_glb.py -- D:\Otherreach_Depot\UE_Exports\SampleAnimationPack_anim --match "Walk_F_Loop"
Every AnimSequence under /Game/<Pack> (-Match on the asset name) -> Animations\<key>.fbx (AnimSequenceExporterFBX, FBX 2020, no
preview mesh, local time) + Manifests\<key>.json (skeleton, play length, frames, rate, root-motion flags). Works under -NullRHI
(SkeletalMesh FBX export does not). 5 packs, 273 clips, 3-38 s a pack. anim_fbx_to_glb.py writes what _retarget_clip.py reads:
the FBX node defaults are the skeleton's reference pose (the GLB rest), cm kept as a 0.01 armature scale, the action "Animation".
_retarget_clip.py needs a provenance.json in the Glb folder (pack, author, licence, url).
Gotchas: FreeAnimationLibrary's walk/jog keep the feet on parallel tracks at the reference pose's 0.28 m (rejected for the player);
FreeAnimationsPack2 references /Game/FightingAnimations2 (missing, harmless); FreeAnimationsPack AS_Rest is lying down. The Demo\
folders hold Epic's own mannequin clips (MM_*, MF_*, Jog_Fwd): Epic sample content, never used.
Packs (FabAssets\Content; all on the UE5 mannequin SK_Mannequin; Fab Standard licence per the owner; never fed to generative AI):
  SampleAnimationPack     "Sample Animation Pack" (fab.com/listings/8de31c5d-93bc-4bd4-9606-ca789ce91b99), 24 mocap clips at 120 fps:
                          unarmed walk F/B/L/R, idle, gesture, injured idle/walk, 90-degree turns, rifle walks/idles, 2 door kicks
  FreeSampleAnimationSet  VanillaLoop "Free Sample Animation Set" (listings/5b737d10-b4cd-4294-8f6b-757b9088039d): one clip each from 15
                          sets - male run, female jog, cover walk, roll, DBNO crawl, item pickup, ladder, lift, push, pull, mining,
                          stairs, locker, flashlight idle
  FreeAnimationLibrary    "Free Animation Library" (listings/422a7c63-5405-4f4d-bbbd-0955d8c0861e): 129 clips - walk/jog 4 directions,
                          crouch, prone, climbing, ladder, ledge, mantle, vault, swim, valves/buttons, sit, sleep, spear attacks 1-6 +
                          spear idle, bike/car, doors, grapple
  FreeAnimationsPack      10 clips: AirKiss, AttackInLevitation2, ChillDriving, ComboHands, EatingOnParty, FlyingUp, GoForward, Rest,
                          RightLegKick2, Talk
  FreeAnimationsPack2     12 clips: BenchConversation3, Crucifixion, DrinkingWineFromGlass, Eats, FightingIdle, GraspLying, Hack,
                          LevitatingFireballCharge2, Row, TakeDown, TakesOutGun, WalkingVideoCall
  Not exported this round: DynamicFalling (10 rolls), Free_Crawl_Animation (10 prone), Free_Interaction_Animation (10 door/lever),
  FreeAnimationSet (dash, dive, glide, zipline; Manny + UEFN) and FabStaging_56\DramaticDeath (11 deaths).
