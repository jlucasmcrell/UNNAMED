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


## Update after the second sheet (gatec2) and the 2x crop

The NPCs are not naked. A 2x crop of the hunter tile (work/crops/cook2.png) shows a real garment with no weave, seam or shading:
Dye in HumGenCreatorAppearance nulled the albedo texture and set a flat colour. At 2 m a Kal's flat ochre read as bare stone and
a Veth's grey top as skin. FIXED in 5540d954: the albedo colour multiplies the albedo texture, so the fabric stays.

Everything else on gatec2 is right: residents on creator bodies (f8cad9b4), distinct creator-quality faces, Kal bald stone with
black folded wings and growths, Siann white-haired, Veth/Orenth natural, 10 NPCs per town at 2 m, no HUD.

Next: gatec3 (queued) proves the fabric fix and prints CREATOR_PART <sex> <slot> -> <asset> per part (1a14ee60).


## After the Gate C verdict (2026-10-29)

Fixes 1 and 2 landed as 6e8c94fe: role wardrobes (coat for rugged roles and stewards, work smock for trades and villagers, robe for the
scholar), and the Kal skin-through-cloth defect traced to four causes (the knit outfit's pale panels, four LODs drawn at once, the
hidden lining, the garment grow). Final sheets: work/creator/out/npc_looks/gatec21, published to first_update/f1_gateC.


## Round 2 of the verdict (2026-10-29 09:30)

- The towns wear the creator wardrobe's period set (shirt, waistcoat, cravat, sash, breeches, boots) dyed per person; Sel wears the robe.
  Modern wardrobes are out, reserved for Otherborn arrivals.
- Kal skin through cloth fixed: knit outfit out, LOD0-only, linings shipped and lining every static piece, cloth 20 mm clear of a body
  that is not grown. 4x crops: work/crops/miner22.png (Kal miner), work/crops/kera24.png (Kera Voss).
- The garb's tricorne is modelled into the shirt mesh, so it stays; the rule that tried to hide it was dead code and is removed.
- Sheets: work/creator/out/npc_looks/gatec25 (identical to gatec24), published to first_update/f1_gateC.
