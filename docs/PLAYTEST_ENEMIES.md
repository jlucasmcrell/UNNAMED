# Playtest enemy art replacements — 18 October 2026

Private branch `art/playtest-enemies` starts at shared `bd34e504`. No shared refs or asset roots are promoted by this work. The wolf, ember hound, boar, creature definitions, combat radii, attack timings, approved player/NPC bodies and accepted run remain unchanged.

| Canon enemy | New model | Appearance / clips |
|---|---|---|
| Bone Walker Husk | `creature_playtest_bone_walker` | Held Cursed Undead Soldier, weathered pale bone / grey-brown grave-rags; glowing eyeball removed. Existing six retargeted clips retained. Its unarmed strike maps to `ability.creature.husk_slash`. |
| Animated Armour | `creature_playtest_empty_armour` | Held Armored Guard Knight, aged iron finish. Dark visor/interior primitive removed at every LOD; rear helmet plate opened. Authored two-handed overhead gauntlet slam maps to `ability.creature.armour_slam`; other five clips retained. |
| Cave Hunting Spider | `creature_playtest_cave_spider` | Held realistic Nephila rig. Native walk; faster walk supplies run; authored small palp/abdomen idle, anticipation/lunge/bite, recoil and leg-curl collapse. Attack maps to `ability.creature.spider_bite`. |

All use the existing creature art/clip path and shipped voice sets. Step aliases use the existing walk/move_fast, walk/move_heavy, scuttle/scuttle_fast families. Existing stone/plate/flesh impact families avoid inventing unsupported audio IDs. No new audio is generated.

## Install for integration

Private candidate root: `D:/Otherreach_Scratch/playtest_enemies_20261018/primary`. Copy only the three new `rigged/creature_playtest_*` folders, the eighteen corresponding `animation/ready/creatures/anim.creature.creature_playtest_*.glb` files, their eighteen `animation/clips/*.json` companions, and the four `manifests/creature_playtest_*.json` / `playtest_enemies.json` records into the main candidate root. The rest of this private root is baseline linking for review. Do not replace the main root's complete bindings/configuration with this stage's `bindings.json`, which has an explicit base-layer player reference for review.

Merge the three additions in `content/_art_bindings.json` through the normal content overlay. All pre-existing entries are semantically unchanged. The full-file overlay snapshot is refreshed only for those three additions; integration with other waiting art changes must re-pin that expected snapshot after semantic review. No save schema or gameplay data changes.

Rebuild licensed derived models and clips with:

```powershell
C:/Python314/python.exe tools/creature_build/playtest_enemies.py --source C:/Otherreach_Recovery/asset_work/creatures/ready_new --out <private-root>
```

The builder reads source GLBs and creates independent derivatives. NumPy, SciPy and Pillow are used for existing-data transforms and explicit rig keys; no generative service is used. Inputs and binary deliverables stay private. The native comparison scene is a review-only helper retained outside production; the shipping change uses the existing renderer.

## Scale and combat body fit

Gameplay radii stay Husk0.35m, Armour0.45m and Spider0.50m. Sampled idle torso-core horizontal extents are0.216m and0.251m for the humanoids; their circles include normal shoulder/arm clearance while attack hands can reach outside. The 1.97m Husk and1.86m Armour stand at their source-converted size beside approved player Average.

The spider is uniformly2.7x its converted0.8m-span source with its origin centred on abdomen/thorax. Its core reaches0.464m horizontally, within the unchanged0.50m radius; the widest idle leg tip reaches1.696m. Legs are appendages outside the gameplay body target, not extra collision circles. Idle height is0.82m. Walk/run metadata carry2.43/5.5m/s paces for the existing runtime speed scaling. These are inherited/retimed authoring paces, not a claim of a new measured zero-slip gait certification.

## Review evidence

`C:/Users/jluca/Documents/Codex/2026-10-18/new-design-project-for-otherreach-from/outputs/Playtest_Enemies_20261018/review.html` holds one idle/attack/death sheet per enemy beside the approved player, individual native frames and the open-helm close-up. These are the native Godot renderer using actual game bindings/clips on a lit review floor, not live combat simulation. Visual acceptance belongs to Claude/owner.

Final production Release build: zero errors /20 existing warnings. Presentation243/243, Architecture23/23 and overlay gates4/4 pass. Content lint:420 definitions, zero errors; xref exit0. Native captures:10 Husk,11 Armour (including rear helm) and10 Spider frames, all final logs without ERROR. The final42-file manifest verifies and all21 reviewed model/clip binaries remain byte-identical after metadata finalization.

All three sheets were inspected as images. The Husk shows exposed pale skull/limbs with grave-rags; Armour has a visible empty rear helm and overhead slam; Spider retains the realistic long-legged body plan and shows anticipation, bite/lunge, running pose and leg-curl collapse. These remain review candidates until Claude/owner accepts them.

The first Husk checkout run logged missing vendor add-ons and a capture-source encoding issue. Both were fixed and Husk was refreshed successfully; that earlier log is retained separately. No temporary capture helper ships in this commit. Final visual findings are recorded with the sheets and `CODEX_PROGRESS.md`. The source models are read only; no new generative mesh, texture or animation service is involved.

## Attribution

- **[Cursed Undead Soldier Rig — DM-913](https://www.fab.com/listings/cf236a92-88d9-4f0d-af10-d4f878560b39).** Held Sketchfab conversion and acquisition metadata record CC BY4.0. Changes: pale bone/grave-rag texture treatment, glowing eyeball removal, conversion/LODs; existing motion retargeted from Quaternius UAL Standard (CC0).
- **[Armored Guard Knight Rig — DM-913](https://www.fab.com/listings/dfc7d8fe-6b8d-476c-876a-7574dbe023d4).** Held acquisition record: CC BY4.0. Publisher credit verified from the listing. Changes: empty visor, open rear helm, aged iron and authored gauntlet slam. Other motions: Quaternius UAL Standard, CC0.
- **[Nephila pilipes spider — huangyuanqiang](https://www.fab.com/listings/4265106f-e4d6-4b82-88ab-5f54c359e77c).** Held acquisition record: CC BY4.0. Publisher credit verified from the listing. Changes: conversion/LODs, uniform size/origin, native walk retime and explicitly authored missing states on its existing Auto-Rig Pro rig.
- Approved player reference: Epic Games MetaHuman, held native Creator export. This work does not modify it or its accepted clips.

Retain source licence notices and attribution when packaging. Do not commit raw licensed source/model/texture files or feed them to generative tools.
