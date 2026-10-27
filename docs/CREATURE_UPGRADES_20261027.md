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
