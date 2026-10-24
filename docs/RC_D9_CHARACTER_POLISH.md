# RC d9 character polish

RC d8 candidate03 (089c97cd) is the accepted owner build. Keep its game, cast and motion payload frozen; changes below belong to the next candidate on `playtest/humgen-main`.

## NPC outfit clones

The accepted native cast already has several fitted outfits, but four NPC bodies repeat a player's exact combination. Replace those combinations through HumGen's documented `human.clothing.outfit.set()` wear API, preserving each identity, sex, age, stature, hair and footwear. These presets are in the Claude-accepted 142-item catalogue at `C:\Otherreach_External_Assets\HumGen_Wardrobe\HUMGEN_WARDROBE_CATALOG.csv`.

| Body | RC d8 combination | RC d9 catalogue preset |
| --- | --- | --- |
| npc_pool_04 | Ada's Smart_Casual: sweater and chinos | female/Casual/Relaxed_Weekday: T-shirt and jeans |
| npc_tam_crowden | Ada's Smart_Casual: sweater and chinos | female/Casual/Stylish_Casual: overshirt, T-shirt and jeans |
| npc_renn_vale | Mateo's Casual_Weekday: T-shirt and jeans | male/Casual/Relaxed_Office: T-shirt and chinos |
| npc_pool_01 | Mateo's Casual_Weekday: T-shirt and jeans | male/Casual/Weekend_Warrior: sweater and jeans |

Export candidates to a separate asset root. The existing native Godot export recipe and LOD material repair apply; preserve original image resolution and sparse morph buffers. Before adopting, compare all 59 bone names, hierarchy and rest transforms against the corresponding accepted body, rebuild its exact-target motion metadata/cache, and capture the dressed body walking and idle in game. Catalogue acceptance alone does not accept a new cast export. Do not edit the accepted package or reuse an old skeleton/garment rebind.

## Relaxed idle outside combat

Expose the simulation's current combat activity in `CombatView`, using its existing out-of-combat delay after exchanged blows plus active actions and guarding. Normal player presentation uses this flag to select the weapon-ready stance; owning an equipped sword alone must not put a peaceful spawn in a two-hand guard. The held weapon, native grip and three accepted strikes stay intact. This is a relaxed idle, not a new sheathing feature. A miss can return to relaxed idle once its recovery finishes; damage, including a ticking damaging effect, extends combat through the existing delay.

Regression checks cover a peaceful equipped spawn, guarding, a missed swing, and expiry after a completed exchange. Native creator/spawn captures must show `idle` with the equipped sword and no active combat.

## Creator body-type selection

The current body-type button is pressed/filled and explicitly says `(selected)`. Keyboard focus remains available on either choice without implying it is the active body. The native review records both button states for Ada and Mateo and requires exactly one selected choice matching the preview body.

## HumGen armour visuals — planned

The Hide Vest and other equipped armour currently affect gameplay and appear in the paper doll, but do not replace the baked clothing on HumGen bodies. This remains a disclosed visual gap. The installed wardrobe has no medieval armour; it does not close that gap.

Use owned/free appropriately licensed armour sources with a proper garment refit through HumGen's custom-content/deform-cloth tools. Start with one torso item on Ada and Mateo, gate shoulder/elbow/hip deformation, coverage masks, clipping and LOD transitions in real movement, then expand to the cast and item-to-visual mapping. Preserve each native skeleton and body proportions. Converted MakeHuman garments failed visual review and remain deferred; directly attaching old MetaHuman binds is not an accepted route. Do not claim armour visuals complete until actual equipped-state captures pass Claude's review.
