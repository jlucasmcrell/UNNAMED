# RC d9 character polish

RC d8 candidate03 (089c97cd) is the accepted owner build. Keep its game, cast and motion payload frozen; changes below belong to the next candidate on `playtest/humgen-main`.

## NPC wardrobe — owner attire ruling

The 2026-10-24 owner ruling supersedes wardrobe03 and the cancelled wardrobe04 refinements. Use the least-modern practical catalogue cuts in frontier tones, chosen for occupation and station, with boots for outdoor workers. No default bright blue, sneakers, silk robe, flight suit, CEO outfit or business suit on villagers. Sel uses plain dark shirt/trouser pieces, without a jacket or tie.

[HUMGEN_RCD9_FRONTIER_ATTIRE.md](HUMGEN_RCD9_FRONTIER_ATTIRE.md) records every job → items → colours rationale and the limitations of this interim catalogue pass. Exact selections and material palettes are in `HUMGEN_RCD9_WARDROBE.json`. The new isolated export root is `rcd9_wardrobe05`; source-game captures must be repeated before Claude review. Old wardrobe03/04 frames do not prove this pass.

Preserve the native Godot recipe, original-resolution retained images, sparse facial buffers, all 59 native bones/hierarchy/rest transforms, exact-target motion metadata and signed texture cache. Capture all fourteen NPCs idle and walking, plus Tavar in his normal captive kneel. Use actual normal bindings and label person/people IDs; preserve canonical phase shaders.

DeepSeek separately owns the named Kal Kera, Siann Sel and Orenth Tavar body exports. Do not claim this human-shaped interim inspection proves those new proportions.

## Relaxed idle outside combat

Expose the simulation's current combat activity in `CombatView`, using its existing out-of-combat delay after exchanged blows plus active actions and guarding. Normal player presentation uses this flag to select the weapon-ready stance; owning an equipped sword alone must not put a peaceful spawn in a two-hand guard. The held weapon, native grip and three accepted strikes stay intact. This is a relaxed idle, not a new sheathing feature. A miss can return to relaxed idle once its recovery finishes; damage, including a ticking damaging effect, extends combat through the existing delay.

Regression checks cover a peaceful equipped spawn, guarding, a missed swing, and expiry after a completed exchange. Native creator/spawn captures must show `idle` with the equipped sword and no active combat.

## Creator body-type selection

The current body-type button is pressed/filled and explicitly says `(selected)`. Keyboard focus remains available on either choice without implying it is the active body. The native review records both button states for Ada and Mateo and requires exactly one selected choice matching the preview body.

## HumGen armour visuals — planned

The Hide Vest and other equipped armour currently affect gameplay and appear in the paper doll, but do not replace the baked clothing on HumGen bodies. This remains a disclosed visual gap. The installed wardrobe has no medieval armour; it does not close that gap.

Use owned/free appropriately licensed armour sources with a proper garment refit through HumGen's custom-content/deform-cloth tools. Start with one torso item on Ada and Mateo, gate shoulder/elbow/hip deformation, coverage masks, clipping and LOD transitions in real movement, then expand to the cast and item-to-visual mapping. Preserve each native skeleton and body proportions. Converted MakeHuman garments failed visual review and remain deferred; directly attaching old MetaHuman binds is not an accepted route. Do not claim armour visuals complete until actual equipped-state captures pass Claude's review.

The detailed approach and first acceptance gate are in [HUMGEN_WORN_ARMOUR_PLAN.md](HUMGEN_WORN_ARMOUR_PLAN.md). It includes the prerequisite audit for skin already removed by baked clothing masks. This task implements no armour visuals.
