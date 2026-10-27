# Creature appearance upgrades — batch A

Owner/Claude lineup verdict 2026-10-27 15:35; canon/Git ruling16:00. Branch `codex/creature-upgrades-d5`, base `first-update-integ02`1d078ad1. No release promotion.

Canon selections and provenance committed and pushed as **a542b0c** on `otherreach-canon` branch `codex/creature-asset-selections`: `canon/bestiary/PLAYTEST_CREATURE_ASSETS.md`. This records the later approved replacements as pending, not shipped.

Reviewed `C:/Otherreach_External_Assets/mob_library/MOB_LIBRARY.md` and `asset_library/INDEX.md`, `mobs.md` first. Catalog row IDs below are from that2026-10-27 snapshot; exact source IDs/paths survive catalog regeneration.

| Subject | Catalog row | Correction / selected source |
|---|---|---|
| Timber Stag |428 `creature wm buck` | Owned WildMesh `SK_Deer.fbx`. Clear active imported pose before converting bone-parented antlers to a skin. Body UV0 was collapsed; copy the authored detailed UV channel into runtime UV0. Native2K body/antler maps retained. |
| Timber Hind |425 `creature wm doe` | Existing source and rig retained; remove green procedural mottle and hue shifts. |
| Lantern Fox |172 `creature wm red fox` | Existing detailed2K `T_Fox_Body.png` retained; remove procedural dark bands. The256² `T_Fox_Fur.png` is a black cutout mask, not the body atlas. Do not invent detail by upscaling it. |
| Hound Pup |170 `creature wm grey wolf b` | Claude's permitted wolf alternative:0.50–0.55 model scale, broader/shorter head, enlarged paws via native named bone pose scales. No rest edits or replacement C1 clips. Row180 grimhound examined but not selected for this juvenile canine. |
| Barrowback |421 `creature wm barrowback` | Shorten both isolated tusk components to0.63 length about their native sockets at all3 existing LODs. Keep skin/rest and full source maps. |

Native source root: `C:/Otherreach_Recovery/asset_work/creatures/_src/` (read only). Selected map paths: `m100/Deer/Textures/T_Deer_Body.png`, `T_Deer_Antler.png`; `m100/Fox/Textures/T_Fox_Body.png`; original mammoth maps under `m100/Mammoth/Textures/`. Shipping relative model slots stay `rigged/creature_wm_buck/creature_wm_buck_rigged.glb` and `rigged/creature_wm_barrowback/creature_wm_barrowback_rigged.glb`.

Private native delta: `D:/Otherreach_Scratch/creature_upgrade_d5_20261027/batch_a_payload`. Install these files and merge its texture-cache manifest **by key** into a new private candidate, preserving the source signature/DDS pairs. Do not copy a complete old world payload or overwrite an accepted package. `BATCH_A_TEXTURE_GATE.json`:15 full-resolution image references,0 failures, native BC7/mipmaps. No new savings measures, reduced textures or dropped LODs.

Same23-definition real-art noon lineup for review: `C:/Otherreach_External_Assets/first_update/creature_batch_a/review.html`, originals and one contact sheet. Five subjects changed,18 retained; the custodian is still the explicitly labelled existing fallback until its replacement batch. Native log137 assets/0 withheld.21 cameras3m from centre; eel/10m warden3m outside posed bounds, exactly as the disclosed baseline. Initial flat-UV stag frame preserved privately; the final sheet uses the corrected native frame.

The assembly includes the review-only Sedge source to show all23 current definitions. It is not a merge recommendation for Sedge gameplay. Production commit changes only creature bindings, bone-look parsing and credits. Existing scalar bone looks retain their prior behavior; XYZ arrays apply only to the juvenile pup. Full source shaders/geometry unchanged for retained creatures. This is an appearance gate, not motion acceptance.

Credits: existing WildMesh `A-W01` / `creature_wm_` entry covers these sources; added modifications line in `content/credits.yaml`. No new item/material lore, location, race or mythology facts. Existing C1 move sets and source clips remain; animation depth and the other owner-selected rigs/HumGen mobs are subsequent batches.

## Batch B — source-quality creature candidates

Batch A6412dfe7 remains immutable. Batch B installs only a new private art delta; it does not promote a public build. Review: `C:/Otherreach_External_Assets/first_update/creature_batch_b/review.html`.

| Subject | Native source / change |
|---|---|
| Cave spider | Matching Nephila native4K PBR maps; existing mesh, skin and LODs. |
| Matriarch | Mixall tarantula, distinct skin,3m BODY length. Native1,428 triangles/70 joints/2K diffuse/six actions; legs extend beyond that measurement. No invented reaction/turn takes. |
| Animated armour | Full native DM-913 knight plate,21,433 LOD0 triangles/original2K PBR; existing native rig and C1 takes. |
| Tender / Warden / Custodian | Stan native28-joint rig and C1 clips;2K stone maps plus CC0 Rock064 normal, native Blender bevel/chipped surface. The556-triangle source is deliberately blocky;13,632 derivative LOD0 body triangles do not make it a high-detail hero source. Warden uniform2.5 scale, other two native4m. |
| Barrowback | Left tusk another15% shorter than accepted Batch A, socket retained at all3 LODs. |
| Raider | Native HumGen Cole, aged52, native Pirate garb and boots, scarf, DM-913 left plate pauldron fitted with HumGen's bilaplacian custom-content API; native4K skin/clothing, original body/rest, ordinary4 LODs. Existing credited Cosmos sword/sheath back carry. |
| Drowned | Native HumGen Nico, aged58, weathered Pirate garb and kelp strips, desaturated pale waterlogged/salt skin baked through native HumGen; source pores/age/normals retained. Same native quality recipe. |

Exact source paths/catalogue keys and licences are recorded in the canon repo's `canon/bestiary/PLAYTEST_CREATURE_ASSETS.md`; `BATCH_B_CATALOG_SNAPSHOT.json` preserves the reviewed catalogue snapshot. Current catalogue row numbers may change. Credits include Mixall, huangyuanqiang, DM-913, ShyrAssets (CC BY4.0), ambientCG Rock064 (CC0), licensed HumGen and existing Cosmos/Quaternius attribution.

HumGen creature libraries use native Epic IK Retargeter → GLTFExporter transport onto each final body:48 owned approved source states +7 aliases =55. Native rest/finger/pace/direction guards and independent body SHA/no-Jaw/no-KayKit readbacks remain mandatory. Runtime bindings alias these baked states before AnimationPlayer/FootPlants setup; they do not retarget or change rest data. Drowned idle/walk use native UAL zombie takes. C1 move definitions/timing are unchanged. Hand grip/fill use the accepted character helpers. High/Ultra retain full source mob textures; ordinary BC7/mip/tier/LOD rules remain, with no new savings.

Review combines the successful full23 B1 lineup with only the two polished HumGen retakes. Exact original-frame hashes/camera/material readbacks accompany the sheet. Large eel/construct/spider subjects use a disclosed3m surface stand-off; others3m centre. Earlier native heap-corruption evidence is retained; the final completed capture is separate, and no isolated cause is asserted. This is appearance evidence, not full combat-motion acceptance. Animation-depth expansion is handed to DeepSeek after Batch B.
