# M3f status - Gathering and one profession

**Date:** 2026-09-24. **Branch:** `claude/phase1`. **State:** implemented and verified; Phase-1 subset only (the owner's ruling: two recipes and one gather→craft proof loop, with no profession-rank ladder; quality and material properties matter; no "craft thousands of daggers" mastery; meaningful first, discovery and complexity progress; not the future combinatorial laboratory).

## The ruling, as applied

- **Two recipes, one loop**, the content bible's blacksmithing proof (`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` §12): Raw Iron Ore smelted into an Iron Billet at the forge, then a billet and an Ash Haft made into the March Spear at the anvil. They take the place of `PROTOTYPE.md`'s salve and sword temper and prove the same two things: exact resource consumption (C13) and a property that lands on the made instance, not the definition (C14's point).
- **No ranks.** Nothing gates a recipe but knowing it and having its station and materials to hand. A novice may try the spear and will often make a crude one.
- **Quality and materials matter.** The weakest material caps the work; the smith's skill against the recipe's complexity moves it a step up or down; a weapon's quality changes its damage.
- **No grinding.** Work far below the smith's hand teaches nothing once its novelty is spent, and only the first of each thing made earns level XP (AG-7).
- ROADMAP M3f was rewritten to this scope before any code.

## What M3f built

| Layer | What | Where |
|---|---|---|
| Domain | `NodeDefinition` (the item one harvest yields, charges, `Respawn` none or daily, the skill it trains at a difficulty), `RecipeDefinition` (station kind, skill, complexity, inputs, one output, quality roll, first-time XP), `Quality` (crude -1, standard 0, fine +1), `CraftingConstants`, and `CraftingRules` as pure functions: the fine and crude chances, the roll, and whether a node is ready | `src/Domain/Crafting/Crafting.cs` |
| World | `GatherCommand` and `CraftCommand`. `GatheringSystem` owns the node records (`StateSlice.Nodes`); `CraftingSystem` owns nothing. Both hand the inventory one all-or-nothing `ExchangeItems`, so a refused craft spends nothing. Quality is on every stack - carried, in a container, on the ground - and stacks of different quality never merge. A weapon's quality adds 2 damage a step. The generator's authored nodes put the region's nodes in their cells' baselines. Events: `NodeGathered`, `ItemCrafted`; view: `NodeView` | `src/World/Runtime/Crafting.cs`, `Items.cs`, `Combat.cs`, `src/World/Generation.cs`, `WorldDelta.cs` |
| Content | `config.crafting`; `skill.smithing`; `resource.ore.iron`, `resource.wood.ash`; `node.ore.iron_seam`, `node.wood.ash_stand`; `recipe.smithing.iron_billet`, `recipe.smithing.march_spear`; `item.material.ash_haft`, `item.weapon.march_spear`; the ore and ingot renamed on screen to the bible's Raw Iron Ore and Iron Billet; survival's +1 yield at level 3; the region's `nodes` and `stations`; both recipes in the starting package. `CraftingContent` builds and lints them (CRF001; WLD010, WLD011). 84 definitions | `content/`, `src/Content/CraftingContent.cs`, `WorldContent.cs` |
| Persistence | **Schema 9**: every stack's `quality`, required and range-checked. The 8 -> 9 step gives older stacks standard; the schema-8 shapes are frozen (`Sections/SchemaV8.cs`). A node's record was already saved (`last_harvest_tick`, `harvest_seq`). The session registers the baseline transition for the two cells that gained a node, so a pre-M3f save loads onto them | `src/Persistence/`, `src/Application/GameSession.cs` |
| Presentation | The seam in its rock and the stand of young ash, each drawn spent while the simulation says so; a stone hearth with its fire and an anvil on a stump. [E] gathers from a node in reach, or opens a station: the panel lists what the character knows to make there, its complexity against the smith's skill, what it takes against what is carried, and a Make button once everything is to hand. Items read "Fine" or "Crude" by their stack's quality. Toasts for each harvest (and a worked-out seam) and each thing made. A two-handed weapon is held level in both hands and thrust | `src/Presentation` |

## How the loop works

- **Where.** The iron seam is on the south face of the iron shelf's rock (64, 178.9), guarded by the husk that walks the shelf and, 12 m east, the animated armour's post. The ash stand is in the east woods at the bible's own (180, 138), on the east pack's ground. The hearth and the anvil stand in the forge shed, 4 m apart: each recipe needs its own station within 1.6 m of the body, the same reach as picking something up.
- **Gathering.** A strike of the seam gives 1-2 Raw Iron Ore; it holds three strikes and never refills. The stand gives one Ash Haft a world day; it grows back at the next world-day boundary (57,600 ticks). The yield is rolled from the node and its harvest, so the same strike gives the same ore whoever makes it; from survival 3 every harvest gives one more. A harvest trains survival at the node's difficulty (seam 5, stand 2).
- **Smelting and forging.** The billet (complexity 0) takes one ore at the hearth; the spear (complexity 10) takes a billet and a haft at the anvil. The best carried stacks are spent first. Both are instant. Both train smithing at their complexity; the first billet earns 15 level XP and the first spear 40, once each.
- **Quality.** The output starts at its weakest input's quality and moves at most one step. At the recipe's complexity one piece in ten comes out a step finer, plus 2% per point of skill past it (up to 60%); for each point of complexity past the skill, 3% come out a step cruder (up to 40%). A novice's spear is crude 30% of the time and never fine; a smith 20 points past it makes a fine one half the time and never a crude one; a crude billet holds even a master to standard. A fine spear hits 12-16 and a crude one 8-12, against the standard 10-14.
- **What teaches.** Work teaches while the skill is less than 15 points past its complexity (or a node's difficulty); beyond that margin only the first success of each output or node (its novelty) does. Repeating trivial billets or strikes teaches nothing.

## Divergences from the content bible (recorded, not rebuilt - the M3d ruling)

| The bible | M3f | Why |
|---|---|---|
| The iron vein at (28, 45) in the Blackvein Cut quarry (§7) | The iron seam at the M3 iron shelf (64, 178.9) | The M3 outpost stands on the bible's quarry cell; M3d kept the working geometry and put the shelf's guardians there. The seam goes with them |
| Kera's smithy at (58, 142), forge and anvil in the settlement cell (§5, §23) | The hearth and the anvil in the M3 forge shed (60-70, 30-38) | The same: the working M3 outpost holds the forge shed |
| Kera teaches the craft in Quest 1, "Iron Under Ash" (§15) | Both recipes are in the starting package | There are no NPCs until M4, and the quest is M5's; moving them to Kera is a content change then |
| The Ash Haft at (180, 138) | The same place | - |
| `PROTOTYPE.md`'s salve recipe, sword temper and herb patch | Not built | Replaced by the bible's pair under the ruling; the salve stays a found consumable |

The layout reconciliation toward the bible's four cells is due before M6 acceptance, as recorded in `M3D_STATUS.md`.

## Exit criteria (`ROADMAP.md` M3f, as rewritten)

| Criterion | Evidence |
|---|---|
| The loop plays end to end: gather both materials, smelt, forge, equip, and fight with the result | `TheLoop_PlaysEndToEnd_GatherSmeltForgeEquipFight`: one character in one world, every step a command - strikes the seam until it is worked out, walks to the stand and cuts a haft, walks back through the outpost gate, opens the forge shed, smelts at the hearth, forges at the anvil, takes the spear in hand and kills a wolf with it (every blow the spear's). The windowed capture below plays the same loop in the real game with the region's creatures in it |
| A craft consumes exactly its recipe and needs the recipe known, the station in reach and the inputs carried | `Crafting_SpendsExactlyItsInputs_AndMakesExactlyItsOutput` (3 ore -> 2 ore and a billet, nothing else touched; a billet and 2 hafts -> a spear and 1 haft; a refused craft spends nothing), `ARecipe_NeedsKnowing_ItsStation_AndItsMaterials` |
| Quality varies with skill and materials and lands on the instance | `Quality_ComesFromTheSmithsHand_CappedByTheWeakestMaterial` (ten spears each: a novice's include crude and never fine, a master's include fine and never crude, a crude billet caps a master at standard, the best stack is spent first; every `ItemCrafted` quality is the made stack's), `AFineSpear_HitsHarder_AndACrudeOneSofter` (8-12, 10-14, 12-16; on the same thrust and roll, crude < standard < fine), `CraftingRulesTests` |
| First-time-only production XP; trivial crafting and gathering teach nothing past the gate | `OnlyTheFirstOfEachThingMade_EarnsLevelXp` (three billets: one award of 15), `WorkFarBelowTheHand_TeachesOnlyItsFirst` (a novice's billet teaches; a smith at 20 gets the novelty once, then nothing, and stays 20; a survivalist at 25 striking the seam likewise) |
| Harvested-node state, the seam's depletion and the stand's refill, survives save/load | `WhatWasHarvested_SurvivesASaveAndLoad` (two strikes saved and loaded: identical digest and node views, one strike left; a haft cut today is still cut after a load and grows back when the day turns), `TheSeam_GivesThreeStrikes_ThenIsWorkedOutForGood`, `TheStand_GivesAHaftADay_AndGrowsBackAtTheDayBoundary`, `Node_HarvestedAgain_CountsItsHarvests` |

Also proven: the two nodes are in the world's baseline where the region puts them (`TheHollow_HasItsSeamAndItsStand_InTheWorldsBaseline`), and an authored node changes only its own cell's baseline while a profile without one keeps the pinned fingerprint (`AnAuthoredNode_IsInItsCellsBaseline_AndChangesOnlyThatCell`); survival 3 takes exactly one more from the same strike (`ASurvivalistTakesOneMore_FromLevelThree`); a save from before the nodes is refused against today's baseline without the transition and loads with it, keeping what it held (`ASaveFromBeforeTheNodes_LoadsOntoTheBaselineThatHasThem`); schema 8 migrates to standard quality everywhere, and a schema-9 stack without a quality, or with one out of range, is corruption (`Schema8To9_GivesEveryStackStandardQuality`, `ASchema9StackWithoutQuality_IsCorrupt_NotDefaulted`); all nine historical fixtures load and migrate to their expected state, v9 with a fine sword, a fine dropped stack and a crude stack in a chest; the CRF001 lint refuses a daily node of more than one charge, a node needing a tool, a recipe of two outputs or none of its inputs, a profession on a recipe, quality tuning past 100%, and a yield passive that is not an addition.

## Decisions (flippable)

1. **The numbers** are placeholders with a stated shape (`PROTOTYPE.md` A-5): charges and yields, the difficulties (seam 5, stand 2) and complexities (billet 0, spear 10), the quality slopes and caps in `config.crafting`, 2 damage a quality step, and the first-time XP (15 and 40).
2. **Three quality steps on the stack.** Crude, standard and fine are enough to make materials and skill matter and to prove the property is the instance's. Only weapons read quality in Phase 1.
3. **Instant crafts.** No crafting time or job queue: the station, the materials and the knowledge are the gates.
4. **The recipes are known from the start**, until the smith can teach them (M4).
5. **The spear trains no weapon skill.** Phase 1 has one weapon skill, the one-hand blade; a polearm family is a later content question.
6. **The yield belongs to the node and the harvest**, so a reload cannot reroll a strike; survival adds to it.
7. **The stand refills at the world-day boundary after its last harvest**, derived from the saved tick: nothing records the refill.
8. **Stations are not obstacles.** The body walks through the hearth and the anvil's greybox; they are scenery for the simulation, which only asks for one of a kind within reach.
9. **The transition** for pre-M3f saves is registered by the session from the fingerprint of the baseline without authored nodes, and carries every record: nothing a pre-M3f save holds was a node.

## Verification

- `dotnet test` (from `src/`): **571 passed**, 0 failed. Architecture 14, Content 102, Domain 129, EntityRegistry 23, World 60, Persistence 138, Application 105.
- Content lint: 84 definitions, 0 errors.
- Godot 4.7.2 headless smoke `PASS`.
- The windowed `--ui-shots` run (ASTRAL) plays the loop in the real game after M3d's and M3e's pictures, fighting whatever hunts the character on the way and keeping out of the armoured sentinel's reach: `seam.png` (the seam and its prompt; the husk killed on the way), `seam_worked.png` (three strikes, "the Iron Seam is worked out"), `stand_cut.png` (the east pack fought off, the haft cut, the stand's stumps and its prompt), `hearth.png` and `smelted.png` (the hearth's panel, the billet made, +15 XP), `anvil.png` and `forged.png` (the anvil's panel, the spear made, +40 XP), `spear.png` (the spear held level; in one run a novice's roll made it a Crude March Spear, named so on the HUD), `thrust.png` (a stray wounded by it), then the death recap at the den. The ending changed: a stray wounded by the spear falls below its flee threshold and runs rather than finishing the character, so the run now wounds one of the den pack and stands.

## Findings

- **The spear may trivialise the den.** In one capture a level-2 character fighting back with a standard March Spear cleared the den pack of four and walked away with 85 of 120 health. Its 2.4 m reach outranges a wolf's 1.5 m bite, and it hits harder per second than the rusted sword. `PROTOTYPE.md` C10 wants the den to need the sword, the bow and the three workings; this is flagged for M6's tuning pass (spear numbers, or the pack's behaviour), not changed here.

## Not done, and why

- Profession ranks, discoveries and experiments, crafting time, tools, repair, and the combinatorial laboratory are outside the ruling.
- Kera, her quest and her teaching of the recipes are M4 and M5 content.
- The bible's placement of the iron and the forge waits for the layout reconciliation before M6.
- `PROTOTYPE.md` C12-C14 are also fresh-session playtest criteria; M6's playtest reads them against the loop above.
- The M3 performance gate still waits on the RAZER window.
