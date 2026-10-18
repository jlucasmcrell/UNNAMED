# Inventory organisation: bags, sorting, junk, the stash

Owner request, 2026-10-18: "we'll need to be able to organize inventory, multiple bags, purchased or crafted, different bags for different crafting ingredients (maybe.. unless there is a better way?)". The standard set, built as commands over the existing item system; nothing pauses, and every move is a command judged by the simulation.

## Bags

- The pack is a **backpack** (`config.inventory stack_slots`, 24) plus **four bag slots** (`PackLimits.BagSlots`). A bag is an item (`category: bag`, `bag: { slots, accepts }`) carried like any other and *equipped* into an empty bag slot with `EquipBagCommand`; `UnequipBagCommand` takes it out and is refused when what it holds would not fit in the rest of the pack.
- A bag has a **quality** (an instance's, from the bench) and a **capacity**: its definition's slots plus one per step of quality (`config.inventory bags slots_per_quality_step`), never fewer than one.
- **Weight and the laden band are unchanged.** A bag adds slots, not carrying capacity; it weighs what it weighs whether worn or carried. An equipped bag takes no slot itself, and cannot be sold, dropped, stashed or spent while it is equipped.
- **Specialised bags** hold only their kinds (`accepts`: `material`, `herb`, `ammo`) and give more slots for the weight than a general bag: the Reagent Satchel (materials and herbs, 12), the Quiver (arrows, 4 stacks) and the Herb Pouch (herbs, 8). An item's kind is its `pack_kind`, else `material` for a material.
- **Auto-routing:** a stack with no place goes to the narrowest bag that takes its kind (a herb to the herb pouch before the satchel that also takes herbs), then to the general containers, backpack first, in the first free slot, in identity order. Equipping a specialised bag takes in what is already carried that it takes. A room check runs the same routing, so a full specialised bag falls back to the general ones and "no room" is judged on the whole pack.
- Bought: Durn Skell (satchel, reagent satchel) and Kera Voss (quiver, herb pouch, satchel). Crafted: `recipe.outfitting.*` at a workbench (survival), taught by Durn's dialogue, with the quality roll (a finer bag has more slots).

## Organising

- `SortPackCommand` (type, name, value, weight): partial stacks of a kind merge, and everything not locked is laid out from the first slot in that order, each kind into the bag that takes it. Ties break on name, finer quality, then identity: the same pack sorts the same way every time.
- `PlaceStackCommand` puts a stack in a slot of a bag (swapping with the stack there when that one can take its place). A split (`MoveItemCommand` carried to carried, part of a stack) keeps its marks; a merge needs stacks alike.
- `MarkJunkCommand`, `LockStackCommand`; `SellJunkCommand` at a trader sells every marked stack that is not locked or worn and that the trader buys. A locked stack is held against sorting and selling; a stack that cannot be sold or dropped (quest items) cannot be marked.
- The inventory screen has the five category tabs (all, gear, consumables, materials, quest: `PackTabs`), a text search, the four sorts, the bag bar (the backpack and each bag slot with its fill), the stash view and, on the chosen stack, lock, junk, split, merge and move-to-bag. Tabs and search are view-only; the section fill never follows the filter.

## Stash

A stash chest stands at the waystation (`container.stash.waystation`, beside the storage chest) and at Cairnwold's lodge (`container.stash.cairnwold`, beside the lodge's chest): a container with `stash: true`, 60 stack slots, an empty loot table. It is an ordinary container (put and take within reach). A crafting station within `stash_pull_radius_m` (60) of a stash **works from it as well as from what is carried** (best make first; carried before stashed at the same make), spending out of the stash in the same all-or-nothing exchange, and the recipe screen shows the source of each input ("3 carried (Reagent Satchel)", "2 in the stash", "1 carried + 2 in the stash", or "have 1 of 3"). This is the standard answer to "a bag per ingredient": one stash that every bench in the settlement reads.

## Persistence

Schema 33. Per stack: its bag, slot, lock and junk; per player: the four bag slots. Older saves migrate with every stack unplaced (the pack places them on load) and four empty bag slots.
