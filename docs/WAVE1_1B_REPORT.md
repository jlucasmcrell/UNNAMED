# Wave 1, WP 1B: item effects and gear enhancement (spec 47)

Branch `claude/wave1-1B`, based on `playtest/humgen-main` at 117f1b8a, which is e38fcdf4 plus six later merges. The branch is pushed. It is not merged and has no PR into main. Rows covered: ITM-19, ITM-20 and ITM-64 to ITM-69. Sources: spec 47 (`47_gear_enhancement.md`, read from the owner's Drive copy), its owner rulings of 2026-10-21, and ATD §1/§3.2.

## What was built

| Row | What now works | Where |
|---|---|---|
| ITM-19 | `equip_effect` on any worn item. While it is equipped, the wearer carries that effect through the existing effect system. Worn effects never run out, never tick, and show as "worn" in the effects panel. Content: Resonance Quartz Ring, ring slot, +1 armour on every region. | `Domain/Items/Items.cs`, `Enhancement.cs` (`GearRules.WornEffects`) |
| ITM-20 | `movement_penalty` is now read. It slows the simulation and movement prediction alike. New `noise` field makes every footfall carry further. Content: lamework jack (6% slower, 25% louder), lame coif (2% / 5%), lame sabatons (2% / 20%), so the full rung-3 kit is 10% slower (ATD §1). | `GearRules.MovementPenalty/NoiseFactor`; seams below |
| ITM-64 | Enhancement slots on weapons and armour. Each piece gets `enchant_sockets` (default 1), plus or minus one per quality step, clamped to 1–3. Heritage items never take enhancements. The record lives on the instance (`ItemEnhancements`: filled slots, cracked slots, settings tried) and travels with the item: pack, ground, containers, trader stock, party sheets. | `ItemEnhancements`, `World/PlayerState.cs`, `WorldDelta.cs`, `Runtime/Items.cs` |
| ITM-65 | Infusions as an `enhancement:` block on a material: cinder sac (ember hound, 20% chance to put Scorched on a wound) and wolf fang (15% bleed). Each one works through an on-hit effect. Heartwood (spec 47's stamina aura, the Old Late quest's reward) is not authored, because that quest does not exist. A heartwood item needs only an `enhancement:` block with `worn_effect`, `visual: body` and a `visual_hook`. | `content/items/material/cinder_sac.yaml`, `trinket/wolf_fang.yaml`, `content/effects/scorched.yaml` |
| ITM-66 | `EnhanceCommand` at the matching bench. Weapon infusions go at the forge with smithing, armour infusions at the workbench with survival, runes at the enchanting table with enchanting. Success chance is 40 + 3 per skill level + enchantability − 10 per tier, clamped to 5–95. The material is spent whatever happens. 35% of failures crack the slot. The roll is keyed by the piece and how many settings it has had, never the tick, so a reload cannot roll again. The setting trains its skill. Previews come from `Simulation.PreviewEnhance` and `Simulation.Enhancement`. | `Runtime/Items.cs` (enhancement section) |
| ITM-67 | `skill.enchanting`, plus four runes made at the enchanting table from a rune stone and an essence: Keen (+5% damage), Warding (+2 armour), Steadfast (+5% stamina regen), Ember-touched (30% chance of Scorched, visible on the blade). A Rune Primer book teaches the four rune recipes. Stability: a piece holds 4 at standard quality, plus 1 per quality step. Above that it is unstable and its wearer carries Unstable (−10% damage). Reforging (`Replace`) consumes the old enhancement whatever the outcome. | `content/items/rune`, `content/recipes/enchanting`, `content/skills/enchanting.yaml`, `config/inventory.yaml enhancement` |
| ITM-68 | Visual hooks under the owner's ruling, enforced by the ITM001 lint. A plain stat bump shows nothing. A property put on a wound shows on the item (`ember`). A worn buff may show on the body. The rule is in place, but no content uses it until heartwood exists. `Simulation.EnhancementVisuals` lists the hooks on equipped gear. **Presentation does not draw them yet** (see open items). | `EnhancementSpec.VisualRefusal`, `GearRules.Visuals` |
| ITM-69 | Salvage recovers each enhancement whole once skill reaches 5 × its tier (the better of salvage and enchanting). The chance is 50% at that threshold, +5% per level beyond it. Otherwise the enhancement is destroyed and leaves its essence (Ember or Resonant Essence). The salvage preview shows the result per slot. Heritage items are refused explicitly ("Heritage is never broken down."), both in `SalvageRules.Plan` and in the view. | `Domain/Crafting/Salvage.cs`, `Runtime/Salvage.cs` |

All numbers are content (`content/config/inventory.yaml` `enhancement:`), with defaults in `EnhancementRules`.

## Save

- Schema 41 adds an optional `enhancements` record on inventory, container and created stacks (`EnhancementsDto`). An absent record reads as never enhanced. The 40 → 41 step moves the version only. Fixture `v41` was written by the new writer and every `expected.json` gained the empty field. All of this is in one commit (1a840598).
- **Schema 40 is a placeholder.** The fixture tests require an unbroken chain and one fixture per version. This branch therefore carries a no-op 39 → 40 step and a `Fixtures/v40` fixture, which is the schema-39 shapes at version 40. **At merge:** drop `SchemaV39ToV40` and `Fixtures/v40`, take the real 40 from the WP that owns it, and renumber this step if the order changes. Both pieces are in the same final commit.

## Seams outside 1B's folders (please confirm)

Each seam is a line or two. The rules stay in `Domain/Items` and `Runtime/Items.cs`.

| File | Change | Owner |
|---|---|---|
| `src/World/Runtime/Combat.cs` | `EffectsOf(player)` adds gear's worn effects. `PlayerHits` applies the main-hand weapon's enhancement procs on a non-magic wounding blow (three lines). | 1A |
| `src/World/Runtime/Systems.cs` | Movement applies `GearMovement`, which is armour's mobility cost (never during a dodge's dash). | shared |
| `src/World/Runtime/Creatures.cs` | The player's footfall radius is multiplied by armour's noise factor (one line). | WP13 area |
| `src/Application/PlayerMotion.cs` | Movement prediction steps with `Simulation.PlayerMovement`, so armour's slowness doesn't rubber-band. | shared |
| `src/World/Runtime/Simulation.cs` | `EnhanceCommand` dispatch, plus read-only `Enhancement`, `PreviewEnhance`, `WornEnhancements`, `EnhancementVisuals`, `PlayerMovement`. | append-only |
| `src/World/PlayerState.cs`, `WorldDelta.cs`, `RuntimeState.cs` | `Enhancements` on stacks, their digests and `PlaceItem`, mirroring `Wear`. | World |
| `src/Presentation/Ui/InventoryPanel.cs`, `EffectsPanel.cs` | Tooltip lines for the equip effect, mobility/noise, what an infusion or rune does, slots, cracks and stability. Worn effects read "worn" instead of a timer. | UI |
| `src/Presentation/Art/art_bindings.json` | Icon entries for the 9 new items, 7 effects and the skill. They reuse existing icons, so real icons are an art follow-up. | art |
| `content/effects`, `content/skills`, `content/recipes/enchanting`, `content/config/inventory.yaml` | The new effects, the skill, the rune recipes and the tuning block. | content |
| `content/loot/bone_walker_husk.yaml`, `ash_ember_hound.yaml` | Appended chances so everything is reachable in a fresh session (`ReachabilityTests`). Husks: rune stone 8%, Resonant Essence 10%, Rune Primer 4%, Quartz Ring 3%. Ember hounds: Ember Essence 15%. Appending at the end keeps every earlier roll as it was. | loot |
| `tests/Architecture.Tests` | The two new read-only `Simulation` methods added to the allowed list. | tests |

## Tests

- New: `tests/Domain.Tests/Items/EnhancementRuleTests.cs` (7), `tests/Content.Tests/EnhancementContentTests.cs` (6), `tests/Application.Tests/EnhancementTests.cs` (10).
- `EnhancementTests` covers: setting at the forge, refusals (wrong bench, wrong target, no material, not an enhancement), failure and cracking, the same-state same-roll guarantee, a rune at the table training enchanting, reforging, worn effects and the ring's equip effect on and off, visuals per the ruling, a wolf-fang proc landing in a real fight, armour slowing the wearer and a wolf hearing the armoured footfall but not the bare one, salvage recovery (whole vs essence), and heritage refused.
- Persistence: v41 fixture, `HistoricalFixtureTests` assertions for schema ≥ 41, completeness sample and coupled fields, and the M6 acceptance save diff allowance.
- Results: see "Suite run" below.

## What the coordinator should film

1. The forge: a rusted sword plus a cinder sac, then Enhance. Show the tooltip's slot line before and after, and a cracked-slot failure at low skill.
2. A wolf fight with an Ember-touched or cinder-infused blade: Scorched ticking on the wolf. With a wolf-fang infusion: bleeding.
3. The lamework kit on versus off: run the same stretch and compare time. Creep up on a facing-away wolf in plate versus bare.
4. Salvage of a Warding-runed piece at enchanting 0 (Resonant Essence back) versus enchanting 20 (the rune back).
5. **The ember visual on the blade cannot be filmed yet.** The hooks exist (`Simulation.EnhancementVisuals`) but nothing draws them (see below).

The enchanting table is not placed in any region yet, so filming the rune steps needs a table placed first (see below).

## Open items and questions

- **No enchanting table in the world.** No region places an `enchanting_table` station; the tests add one. Placing it needs a region-file edit outside 1B. Where should it go: Cairnwold, or the waystation?
- **Sources in play** are provisional and in loot only (see the seams table). Runes come from their recipes, which need the Rune Primer. A trader for rune stones and essences belongs to the economy WP. Heartwood waits for its Old Late quest.
- **Lore teaching runes** (spec 41 tie-in, QST-69) is not built. For now the Rune Primer is the findable source that satisfies TCH001.
- **Enhancement visuals are not drawn.** A presentation pass needs to attach the `ember` hook to the held blade (`HeldWeapon` mount), and any later body hook to the body, in Ultra first. The data and the ruling lint are in place.
- **Not built:** the stagger (boar bristle) and armour-pierce (Blackvein ore) infusions, and the sleep/paralyse/venom reagent procs. The first two need new combat fields in 1A's `Domain/Combat`. The reagents need their material items authored. All of them use the same `enhancement:` block once their effect or material exists.
- Spec owner question 2 is resolved by the 2026-10-21 ruling: enhancements come out only through salvage, by skill. There is no plain "remove and keep".
- The Vaskaal-wielder enchantment question (OPEN-19) is untouched.

## Suite run

Release configuration, each test project on its own, on this branch:

| Project | Result |
|---|---|
| Application.Tests | 685 passed, 1 failed (`ReachabilityTests.EveryItem_CanBeGotInAFreshSession`), 56 min. Fixed in 2adbaf2 (sources in play). The re-run passed: Reachability, Enhancement, Itemization, Salvage, GameSave, Companion, BarrowworksBalance and BehaviourMatrix, 48 tests. |
| Content.Tests | 367 passed (re-run after the fix) |
| Domain.Tests | 431 passed |
| Persistence.Tests | 282 passed, 1 skipped (that skip predates this branch) |
| Presentation.Tests | 434 passed (re-run after the fix) |
| World.Tests | 90 passed |
| Architecture.Tests | 23 passed |
| EntityRegistry.Tests | 23 passed |
| Soak.Tests | 10 passed, 3 skipped (those skips predate this branch) |

Content lint: 0 errors. xref: clean.

The whole Application suite was not re-run after the loot fix, which only appended loot chances. The coordinator's pre-merge full-suite run covers it.
