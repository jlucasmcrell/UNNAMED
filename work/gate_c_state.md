# Gate C, state after the first proof run (`gatec1`, 2026-10-28 22:03-22:16)

## What the run produced (real, on disk)

- `work/creator/out/npc_looks/gatec1/ashen_hollow/` — 10 NPC frames + 3 wides + `npc_looks.json` + `NPC_LOOKS.png` (1536x2152)
- `work/creator/out/npc_looks/gatec1/cairnwold/` — the same, 10 NPCs
- The harness works: `NPC_LOOKS ashen_hollow face routine.ashen_hollow.cook: people.veth f, face.f.asian_presets.asian_2`

## What is right

- The named cast are drawn on the creator bodies: `kera_voss`, `sel_arien` -> `humgen_creator_female`; `renn_vale`, `tavar_orr` ->
  `humgen_creator_male`. Distinct faces, Kal have stone skin and growths, Siann is white-haired, Veth are natural.
- The residents' looks are generated and distinct (`cook` got `face.f.asian_presets.asian_2` from `NpcLooks.Residents`).

## What is wrong, in the order to fix it

1. **Residents were not drawn on creator bodies in this run.** The log shows
   `routine.ashen_hollow.guard drawn ... with model humgen_race_kal_f_compact`: a resident has no NPC file, so
   `NpcLooks.Of(definition, ...)` received a null definition and resolved no look. FIXED in `c8347d55` (people now also come from the
   routine) but NOT yet proven - the next run is the proof.
2. **The creator bodies are bare-chested** (Kera top-left is a bare stone torso with a strap). The outfit part resolves -
   `work/creator/game/world_fallback/rigged/humgen_creator_f_outfit_outfit_casual_smart_casual/` holds its `creator_part.json`, a
   155 MB GLB and its textures - so the failure is in the look reaching `ApplyLook`, not in the art root. Next check: log what
   `LookOf` returns per NPC (null or the record) and whether `ApplyLook` finds the part; the strongest suspicion is that
   `NpcsView.Creator()` (its own catalogue/data load) returns null, which would make every `worn` null and leave the body with the
   binding's `Outfit = []`.
3. Framing: one tile (`tavar_orr`, bottom row) is cropped below the head, and the faces are shot at 2.0 m with the plan's height -
   after (1) and (2) the camera pass should be re-checked tile by tile.

## Art roots this run used

`--asset-root D:\Otherreach_Scratch\art_merged` (world art, **no** `humgen_creator_*` folders) and
`--asset-fallback <worktree>/work/creator/game/world_fallback` (which holds all 157 creator part folders, including the outfits).
`OTHERREACH_CHARACTER_ROOT = <worktree>/work/creator/game/humgen_characters` (holds `humgen_creator_{male,female}_motion.res`).
