# Asset Prompt Risk Audit

**Date:** 2026-09-24
**Scope:** the Phase-1 prompt library

Two prompt failure modes cost real rebuilds during the Phase-1 asset sprint. This records them
against every Phase-1 prompt so the same shape is not reused as a template.

A tag is **not** a verdict that the asset is wrong. Only one asset in this library is a
*confirmed* failure of each mode, and the difference between confirmed and suspected is kept
visible below rather than flattened.

## The two modes

### `FINE_GEOMETRY_RECONSTRUCTION_RISK` - fine geometry the rebuild cannot resolve

`creature_bristleback_boar` failed its image-to-3D pass **twice**, returning a tangle of flat
grey shards with no boar in it. The concept was good - a proper wild boar with tusks, snout,
ears and a mane ridge. The other four archetypes reconstructed cleanly, and the difference is
the surface: hound (short coat), husk (bare bone), armour (hard plate), spider (smooth chitin)
are all solid, high-contrast forms, while the boar was covered in long separate bristles. Hair
strands are thinner than the reconstructor's sampling, so they return as disconnected sheets.

The fix was to describe a **solid matte hide with a low ridge of short stiff hair**. The
rebuilt boar reads unmistakably as a boar.

### `NEGATION_PROMPT_RISK` - naming a part in order to forbid it

`resource_ash_haft` wanted a raw crafting material: the stave the March Spear is built from.
Its prompt said a haft "for a spear" and carried "no metal, no binding and no head". The
concept came back as a **completed spear with a metal head, a binding collar and a butt cap** -
all three forbidden parts, drawn.

The fix was to describe only the bare shaft and never mention a spear, a head or metal. That
produced exactly the pale knot-marked stave that was wanted.

**Write what you want to see, and describe a surface the reconstructor can resolve.**

## Confirmed

| asset | mode | evidence |
|---|---|---|
| `creature_bristleback_boar` | `FINE_GEOMETRY_RECONSTRUCTION_RISK` | Two builds from the original prompt returned flat shards; rebuilt correctly once the long bristles were removed. |
| `resource_ash_haft` | `NEGATION_PROMPT_RISK` | The prompt said 'for a spear' and 'no metal, no binding and no head'; the concept came back as a complete spear with all three. |

## Tagged: same pattern, not proven

These prompts contain the pattern. They have not been rebuilt to test it, so they are flagged
rather than declared broken.

| asset | request file | tag | matched text |
|---|---|---|---|
| `building_lodge` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no plaque, no sign, no writing |
| `creature_cave_hunting_spider` | `overnight_creatures.json` | `FINE_GEOMETRY_RECONSTRUCTION_RISK` | bristl |
| `landmark_ashen_waystone` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no carved |
| `landmark_foldscar_core` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no carving, no lettering, no symbol, not quite |
| `landmark_quiet_stone` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no carving, no markings |
| `npc_siann_archivist` | `races_npcs.json` | `FINE_GEOMETRY_RECONSTRUCTION_RISK` | hair |
| `npc_siann_archivist` | `races_npcs.json` | `NEGATION_PROMPT_RISK` | no hint, no reason |
| `npc_veth_magistrate` | `races_npcs.json` | `FINE_GEOMETRY_RECONSTRUCTION_RISK` | hair |
| `prop_blocked_shaft` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no lettering, no sign |
| `prop_cart_damaged_merchant` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no cargo, no lettering |
| `prop_quarry_rail_track` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no lettering, no markings |
| `prop_quarry_winch` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no lettering, no markings |
| `resource_iron_billet` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no lettering, no maker, no stamp |
| `weapon_arming_sword` | `_remaining_hq.json` | `NEGATION_PROMPT_RISK` | unadorned |
| `weapon_hunting_bow` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no binding, no engraving, no inlay |
| `weapon_march_spear` | `phase1_bible_landmarks.json` | `NEGATION_PROMPT_RISK` | no engraving, no gemstone, no ornament |

## Clean

11 of 27 Phase-1 prompts carry neither pattern.

## Known limitation

A prompt id that appears in more than one request file is tagged only in the first file it is
found in, because the sweep indexes prompts by id. `weapon_arming_sword` is the case in point:
it is tagged in `_remaining_hq.json` and untagged in `phase_a_props_weapons_creature.json`,
where the two entries carry different text. Re-running the sweep after an id becomes unique
resolves it; it is recorded here rather than silently left inconsistent.

## What to do with a tag

- Before **regenerating** a tagged asset, rewrite the prompt first: replace fine separated
  geometry with a solid surface, and replace every negation with a positive description of
  what should be there.
- Do **not** reuse a tagged prompt as a template for a new asset.
- A tagged asset that already built correctly is fine to keep and use. The tag describes the
  prompt, not the mesh.

*Tags written back into the request entries: yes.*
