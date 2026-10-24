# RC d9 character polish

RC d8 candidate03 (089c97cd) is the accepted owner build. Keep its game, cast and motion payload frozen; changes below belong to the next candidate on `playtest/humgen-main`.

## NPC wardrobe variety

Replace the fourteen NPC ensembles through HumGen's documented `human.clothing.outfit.set()` and footwear wear APIs. Preserve each identity, sex, age, stature and accepted head/facial hair. The selections below come from the Claude-accepted 142-item catalogue at `C:\Otherreach_External_Assets\HumGen_Wardrobe\HUMGEN_WARDROBE_CATALOG.csv`; `HUMGEN_RCD9_WARDROBE.json` records exact preset paths and the reproducible colour variation.

| Body | Outfit | Footwear |
| --- | --- | --- |
| npc_kera_voss | f_winter_frosty_evening | f_outdoor_hg_garden_boot_female |
| npc_sel_arien | f_office_pantsuit | f_office_hg_leather_dress_shoe |
| npc_tavar_orr | m_winter_frosty_evening | m_extra_outfits_pack_pirate_boots |
| npc_berrin_garth | m_extra_outfits_pack_on_the_road | m_outdoor_hg_garden_boot_male |
| npc_renn_vale | m_office_open_suit | m_office_hg_suede_dress_shoe |
| npc_durn_skell | m_extra_outfits_pack_bomber_look | m_extra_outfits_pack_pirate_boots |
| npc_mara_ling | f_extra_outfits_pack_ceo | f_office_hg_suede_dress_shoe |
| npc_tam_crowden | f_extra_outfits_pack_presentation | f_office_hg_leather_dress_shoe |
| npc_pool_00 | f_extra_outfits_pack_dress | f_office_hg_suede_dress_shoe |
| npc_pool_01 | m_extra_outfits_pack_pirate | m_extra_outfits_pack_pirate_boots |
| npc_pool_02 | f_extra_outfits_pack_kimono | f_office_hg_leather_dress_shoe |
| npc_pool_03 | m_extra_outfits_pack_relaxed_fit | m_sneakers_hg_suede_sneakers_male |
| npc_pool_04 | f_office_new_intern | f_sneakers_hg_leather_sneakers_female |
| npc_pool_05 | m_office_stock_exchange | m_office_hg_suede_dress_shoe |

Kera wears a brown outdoor coat and work boots; Sel wears a formal pantsuit; Tavar wears an olive winter coat and tall boots; Berrin wears an overshirt, loose work trousers and boots. Tavar's coat uses HumGen's documented `randomize_colors` API with seed 17, changing clothing only. Each named NPC retains a distinct hairstyle. The pool has six distinct garment sets, with varied footwear. Compare garment sets without sex suffixes against **both** player outfits; a different preset name alone is insufficient.

Export into a separate asset root. Preserve the native Godot export recipe, original image resolution and sparse facial morphs. Before adoption, prove all 59 bone names, parents and rest transforms match the corresponding accepted body, repair only LOD material references, rebuild exact-target motion metadata and the signed texture cache, then capture each dressed body walking and idle in game. Inspect Tavar's new coat in his normal captive kneel as well. Catalogue approval does not accept a new cast export.

Wardrobe inspections use the normal NPC bindings and figures on a labelled platform. Choose opaque Veth representatives when a pool also supplies phased peoples, and label the actual person/people IDs in both the frame and receipt. Canonical Mor phase shaders are separate from asset transparency; do not remove a people's appearance to disguise a review-selection error. Audit native body/clothing material alpha and keep the original full-resolution frames.

## Relaxed idle outside combat

Expose the simulation's current combat activity in `CombatView`, using its existing out-of-combat delay after exchanged blows plus active actions and guarding. Normal player presentation uses this flag to select the weapon-ready stance; owning an equipped sword alone must not put a peaceful spawn in a two-hand guard. The held weapon, native grip and three accepted strikes stay intact. This is a relaxed idle, not a new sheathing feature. A miss can return to relaxed idle once its recovery finishes; damage, including a ticking damaging effect, extends combat through the existing delay.

Regression checks cover a peaceful equipped spawn, guarding, a missed swing, and expiry after a completed exchange. Native creator/spawn captures must show `idle` with the equipped sword and no active combat.

## Creator body-type selection

The current body-type button is pressed/filled and explicitly says `(selected)`. Keyboard focus remains available on either choice without implying it is the active body. The native review records both button states for Ada and Mateo and requires exactly one selected choice matching the preview body.

## HumGen armour visuals — planned

The Hide Vest and other equipped armour currently affect gameplay and appear in the paper doll, but do not replace the baked clothing on HumGen bodies. This remains a disclosed visual gap. The installed wardrobe has no medieval armour; it does not close that gap.

Use owned/free appropriately licensed armour sources with a proper garment refit through HumGen's custom-content/deform-cloth tools. Start with one torso item on Ada and Mateo, gate shoulder/elbow/hip deformation, coverage masks, clipping and LOD transitions in real movement, then expand to the cast and item-to-visual mapping. Preserve each native skeleton and body proportions. Converted MakeHuman garments failed visual review and remain deferred; directly attaching old MetaHuman binds is not an accepted route. Do not claim armour visuals complete until actual equipped-state captures pass Claude's review.

The detailed approach and first acceptance gate are in [HUMGEN_WORN_ARMOUR_PLAN.md](HUMGEN_WORN_ARMOUR_PLAN.md). It includes the prerequisite audit for skin already removed by baked clothing masks. This task implements no armour visuals.
