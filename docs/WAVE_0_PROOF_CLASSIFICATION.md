# WAVE 0 — PROOF SET CLASSIFICATION

Full classification of all 15 proof assets, inspected by rendering each with
`_blender_preview.py` and comparing against the intended component.

**Sheets:** `review\proofset_views\sheet_proofset.jpg`, `sheet_proofset2.jpg`,
`sheet_proofset3.jpg`

| # | Asset | Class | What was produced |
|---|---|---|---|
| 1 | `weaponcomp_mace_head_flanged_a` | **FAIL — wrong object** | A smooth hemispherical dome. No flanges, no socket collar. Reads as a helmet crown or a bowl. |
| 2 | `weaponcomp_haft_short_a` | **FAIL — wrong object** | Not a rod. Proportions `[0.30, 0.086, 0.271]`; a squat block. |
| 3 | `weaponcomp_haft_long_a` | **FAIL — extra geometry** | A thin rod **with a large rounded paddle blade attached**, like a canoe paddle. |
| 4 | `weaponcomp_pommel_counterweight_a` | **FAIL — wrong object** | A bulbous gourd shape; no facets, no socket stub. Not a counterweight. |
| 5 | `weaponcomp_blade_arming_sword_a` | **FAIL — completes the object** | A **whole sword** with crossguard, grip and pommel. The requested bare blade and tang. |
| 6 | `weaponcomp_shield_heater_a` | **FAIL — wrong object** | The face is a plausible wooden shield, but the reverse generated as a **red and pink tangled mass** with malformed strap geometry. Unusable. |
| 7 | `armour_chest_plate_base_a` | **FAIL — unresolvable noise** | A shattered, jagged sheet-metal form. Not a wearable object. |
| 8 | `armour_chest_underlayer_gambeson_a` | **BORDERLINE — shape passes, spec violated** | A convincing quilted jacket. But it is a **garment on an invisible body**, not a shell; and the reconstruction implies a body that must not become canonical. |
| 9 | `armour_gorget_plate_a` | **FAIL — completes the object** | A full **helmet** with a neck opening. |
| 10 | `weaponcomp_grip_standard_a` | **FAIL — wrong object** | A rectangular prong beside a leather-wrapped **box**. No cylindrical grip. |
| 11 | `weaponcomp_grip_vaskaal_a` | **FAIL — wrong object** | A narrow boxy form. No 3+2 finger channels. |
| 12 | `armour_kal_back_channel_a` | **FAIL — unresolvable noise** | A **crumpled mass of twisted metal**. No channels, no plate. |
| 13 | `magiccomp_focus_crystal_a` | **FAIL — wrong object** | A small angular shard with a mount; no faceted crystal, no threaded socket. |
| 14 | `weaponcomp_mechanism_telescope_a` | **PASS** | Nested cylindrical tubes with a locking collar ring, correctly shown partly extended. |
| 15 | `weapon_hybrid_focus_staff_spear_a` | **PASS** | Both modes correct: stowed staff with crystal focus, deployed spear with a steel head. |

## Totals

| Class | Count |
|---|---|
| **PASS** | **2** |
| FAIL — wrong object | 9 |
| FAIL — completes the object | 2 |
| FAIL — unresolvable noise | 2 |
| BORDERLINE — spec violated | 1 |

**Success rate from isolated prompts: 2/15 = 13%.**

## The failure taxonomy, and what each implies

The classes are not equally interesting. Three are documented earlier in this file as the
generic failure; they differ in cause:

**Completes the object (2).** The generator sees something partial and resolves it to a
canonical whole: a bare blade becomes a sword, a gorget becomes a helmet. *The sacrificial
stub convention fixes this* — the mace test proved it — because the object then genuinely is
whole.

**Wrong object (9).** The most common. An isolated mace head, grip or pommel has no canonical
form for the reconstruction to lock onto, so it produces something mace-adjacent, grip-adjacent
or nothing at all. *The stub convention addresses most of these* by giving the part a whole
object to live in, but it cannot help where the part has no whole form — see the attachment
hardware note below.

**Unresolvable noise (2).** A crumpled back plate and a shattered breastplate. These share a
trait: **large, thin, curved shells.** Thin-shell geometry appears to be where the
reconstruction fails outright, which is a serious finding for armour specifically, and is the
reason the armour bakeoff is necessary rather than assumed.

**Spec violated (1).** The gambeson is *shaped* correctly but is a garment wrapping an implied
body, not a detached shell — and per the new hard rule, a generated mannequin must never be
the authoritative body surface.

## Which assets a stub can plausibly rescue

| Asset | Stub candidate | Reason |
|---|---|---|
| mace head | **yes** | proven — flanged ball on a haft stub |
| pommel | **yes** | seats on a haft butt; stub is a cylinder |
| grips (standard, Vaskaal) | **yes** | a grip is a haft section between stubs |
| blade | **yes** | blade + short grip stub |
| focus crystal | **yes** | crystal mounted on a staff stub |
| short/long haft | **yes** | a haft *is* the stub; needs no second object |
| shield | **maybe** | reverse face is the failure; a strap-side stub may not help |
| gorget | **test pending** | stub test passed with a bust, but the bust must be discarded |
| chest plate | **no — armour test** | thin curved shell, failed as noise |
| Kal back channel | **no — armour test** | thin curved shell, failed as noise |
| gambeson | **no — canonical shell** | conforming soft layer |

## Attachment hardware — a separate category

Items with no whole form that contains them cannot use a stub at all. These need procedural
authoring:

- socket collars and ferrules as standalone parts
- rivets, buckles, straps, clasps, hinges
- mechanism internals (rails, carriers, linkages)

They are small, geometrically simple, and exactly what a parametric Blender generator does
well. `weaponcomp_mechanism_slide_a` was never built because it is largely this category.
