# M3b status - Items, Inventory, Equipment

**Date:** 2026-09-23. **Branch:** `claude/phase1`. **State:** implemented and verified; Phase-1 subset only (`PROTOTYPE.md` §4).

## What M3b built

| Layer | What | Where |
|---|---|---|
| Domain | Item definitions as the simulation uses them. Equipment slots with attribute and skill minima, never a level (`PROGRESSION.md` §11.1). Loot tables with guaranteed, weighted and independent-chance entries, rolled from a semantically keyed source. Merchant stock and prices | `src/Domain/Items` |
| World | `InventorySystem` owns carried stacks, the purse, items lying in the world and changed containers; `EquipmentSystem` owns the slots. Commands: `MoveItemCommand`, `EquipCommand`, `UnequipCommand` | `src/World/Runtime/Items.cs` |
| Content | `PROTOTYPE.md` §4.2's 15 items, `loot.wolf_grey` and `loot.den_cache`, `merchant.smith_orren`, `config.inventory` (24 stacks, 30 kg + 2 kg per point of Might, the starting kit), `config.economy` (merchants buy at 40%), and the den cache's site in the region. `ItemContent` builds and lints them (`ITM001`) | `content/`, `src/Content/ItemContent.cs` |
| Persistence | Schema 6: the player gains equipment slots and a purse; a created instance gains its count; the entities section gains changed world containers. Frozen V5 player shape, 4 -> 5 repointed at it, the 5 -> 6 step, container items in the definition-ID pass and baseline proof, the v6 fixture | `src/Persistence` |
| Presentation | Items lying in the world and the den chest in the greybox. E opens a container or picks an item up, and Tab opens the inventory panel. Every button submits a command | `src/Presentation` |

## How carrying works

- **One path for every move.** `MoveItemCommand` moves some of a stack between being carried, a container and the ground (S-14's `Transfer`). Every check happens before anything changes, so a refusal leaves every stack, and the container's untouched state, as it was (`TooHeavy_IsRefused_AndTheRefusalTouchesNothing`).
- **Identity follows the stack.** A whole stack keeps its item ID wherever it goes. Part of a stack becomes a new stack with a new ID from the registry. A stack that merges fully into another retires its ID. Everything carried is registered, so one ID held twice is an error at load (D-10).
- **Containers follow M2's promotion rule.** An untouched authored container holds its loot table's result, rolled from `(seed, cell, "loot", key)`, and nothing about it is saved. The first change gives the container and every stack in it identities, and from then on its whole contents are saved as one record, proven against the host cell's baseline hash. `PROTOTYPE.md` §7.4's "the den chest contains exactly the two items left behind, at the same container entity ID" is that record.
- **Equipment names carried items.** Equipping binds, and whatever is displaced stays carried. A two-hander takes both hands. An equipped item must be unequipped before it leaves the inventory; a quest item can never be dropped.

## Exit criteria (`ROADMAP.md` M3b)

| Criterion | Test |
|---|---|
| Inventory transfer | `TheDenCache_HoldsItsLootTable_UntilTouched_ThenEveryStackInItHasAnIdentity`, `DroppingAndPickingUp_KeepsTheItemsIdentity...`, `SplittingAndMerging_WithinTheInventory` |
| Capacity overflow | `TooHeavy_IsRefused_AndTheRefusalTouchesNothing`, `NoFreeStackSlot_IsRefused` |
| Equipment swap | `EquippingTheBow_TakesBothHands_AndTheSwordStaysCarried`, `AnUnmetAttributeMinimum_IsRefused...`, `Unequipping_...` |
| Container persistence across save/load | `ItemsEquipmentAndTheCache_SurviveSaveAndLoad` (state digest, equipment, ground items, the cache's contents and ID) and the v6 fixture |
| Loot distribution over 10^5 rolls | `AHundredThousandRolls_StayInsideTheDesignedBounds` (70% / 45% / 15%, within four standard deviations), `AStaticTable_GivesExactlyItsContents`, `TheSameSource_RollsTheSameResult` |
| Item uniqueness (D-04) | Carried items are registered, and a duplicate ID refuses the load. Whole stacks keep their IDs; partial stacks get new ones |

## Decisions (flippable)

1. **Currency is a purse on the player**, not an item: `PROTOTYPE.md` §4.2's exact 15 items have no coin.
2. **Where the prototype's items meet `DATA_MODEL.md`.**
   - Arrows are category `misc`, and the bow names its ammunition (`ammo_item_ref`).
   - The wolf fang is `misc` with `equip_slot: amulet`: PROTOTYPE's "non-armor, non-weapon slot".
   - The bow has `draw_time` and no `skill_ref`, since Phase 1 trains only `skill.one_hand_blade`.
   - Weapon damage is `[min, max]` (the sword is `[7, 7]`), and `attack_speed` replaces PROTOTYPE's `attack_time`.
3. **The bow requires Might 9.** That proves the gate exists; a new character has 10.
4. **Where items come from, until their systems exist.**
   - The starting kit is the sword (equipped), the bow and the flask.
   - The den cache holds iron ×3, arrows ×12 and salve ×2.
   - The smith's stock is arrows and the hide vest; trade opens with the NPC in M4.
   - Wolves drop by `loot.wolf_grey` once they exist (M3d).
   - The tome's spell grants are added with the spells (M3e).
   - Halda's token is placed by the quest (M5).
5. **Out, per the brief:** durability, sockets, enchantments, set items, familiarity, crafting, and an item-power treadmill (items carry fixed numbers; nothing scales with level).
6. **A changed container stays recorded** even if its contents return to their original state. Its baseline is content, which the save layer never sees; the cost is one small record per touched container.

## Verification

- `dotnet test src/UNNAMED.sln`: **448 passed**, 0 failed. Architecture 14, Content 78, Domain 107, EntityRegistry 23, World 58, Persistence 126, Application 42.
- Content lint: 36 definitions, 0 errors.
- Godot 4.7.2: the headless smoke prints `PASS`. Windowed `--ui-shots` (a scripted run through the real command path) opens the inventory, walks to the den, opens the cache and takes the arrows. The three screenshots are consistent: 4 stacks and 5.6 kg carried after the take, 2 stacks left in the cache.

## Not done, and why

- Buying and selling: prices and stock exist, but the command needs the smith NPC (M4, "merchant interaction on top of the M3b economy stub").
- Using items (the salve's heal, the tome's grants, the flask's charge) arrives with status effects, spells and gathering (M3c, M3e, M3f).
- The performance gate from M3 still waits on the RAZER window.
