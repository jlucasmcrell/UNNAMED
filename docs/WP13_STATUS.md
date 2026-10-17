# WP13 status: NPC adventurers (simulation part)

SYSTEMS_COMPLETENESS_PLAN_20261017 §2.13 ("adventurers") and the WP13 brief in §6; CLOUD_TASKS_WAVE3 §5. Bodies from existing variants and their presentation are local Claude's.

## What is built

- **Adventurers** (`src/Domain/Social/Adventurers.cs`, pure, integer only):
  - each one's slot and generation, name and people (WP2's Suggest names, never invented), home settlement and where they are;
  - level and skills as a `CharacterProgression` on the character's own curve, guards and skill rules (`ProgressionEngine`), points spent on might and endurance;
  - gear as real items with their own instance IDs, worked up a content ladder;
  - coin, a pack of goods to sell or deliver, risk tolerance, condition (active, injured), today's plan, and whether the character has them in the party.
- **Their days**, at each midnight on absolute day boundaries (T-22), the same step online and away:
  - yesterday's plan is resolved by the fixed outcome table on the margin of power (level, ladder pieces, companions) over a ground's danger: death, injury, success, or a day that came to nothing (DN2 amendment: never a simulated fight);
  - the canon goal chain plans today (NPC_SIMULATION:54): deliver what a bounty here wants; sell what this market keeps (what no market keeps the town takes; what another keeps is carried there); buy the next ladder piece, or commission it from the smith out of market stock by its real recipe; buy the day's salve; then a competitive bounty here within their risk, or a hunt for what a bounty wants, or the best ground within their risk; with nothing here, or a pack nobody here buys, the Old East Road;
  - those out after the same ground from the same settlement group up (power for each companion);
  - retirement past a wealth or an age: into an open role slot of their settlement (the NPC life vacancy is promised to them and they arrive in it under their own name), or out of the region;
  - arrivals refill each slot 3-7 days after a death or retirement (DN4), keeping each settlement at its target of 4 (DN5: a target, never a cap).
- **What a day does elsewhere**, always by command or hook:
  - competitive bounties (WP4's `competitive: true`) claimed through `QuestSystem` (`ClaimBounty`): first claimant wins, in key order among adventurers, and a bounty the character holds is lost to them; the board names the claimant;
  - hunts cull a spawner's creatures through `CreatureSystem` (`CullCreatures`): only out of sight (never tier A), only from spawners with a respawn timer, no corpse, no XP, deed or defeat flag; their timers bring them back;
  - trade goes to the markets through `IAgentTrade`: sold goods join a market's stock, bought and commissioned goods leave it (never remembered against the character's sells), and what they pay goes to the merchants' purses as the market settles the day;
  - retirements go to NPC life (`SettleRetiree`).
- **Bodies**: an adventurer in town (resting, injured, or before they leave at 08:00) or with the party has an NPC body at their settlement's lodge (`config.adventurers`); one out, dead or retired has none. Bodies are made and taken away only out of sight.
- **Asking for company** (WP16's LFG): an adventurer going out may post a `SettingOut` 30 game minutes before leaving (always at least 20), read by tick (`ISettingOutSource`), joined to the setup's source. When the character answers and is at the lodge when they leave, the group forms and the adventurer goes with the character as a **temporary ally** until midnight; a bounty they set out on that the character finishes splits its purse; then they part and take their own life up again.
- **Hiring** (WP15a): every adventurer is hireable as a guard or porter with the config's body. Hired, their own progression and gear become their party sheet (`AdoptSheet`), so they keep their level and gear; dismissed, they take the sheet back (gifts included, loans returned) into their life. A hired adventurer who falls dies (DN9) and their slot waits for an arrival; one who falls as an ally is wounded and rests.
- **Offline catch-up**: `AdventurerSystem : IAbstractAdvance`, stepped per day, placed after the weather and before the creatures, NPC life and the markets in the advance order so each sees what the adventurers did as it settles.
- **Content**: `content/config/adventurers.yaml` (settlements, grounds, lodges, roads, outcome table, ladder, prices, LFG, retirement, hire body); the hunting bounties `road_wolves`, `crag_walker`, `winter_hides` and `lodge_supplies` are now `competitive: true`; ADV001-ADV003 lints (shape; references; lodges standable and reachable).

## Tests

- `tests/Domain.Tests/Social/AdventurerTests.cs`: slots to target, the outcome table and risk, hunts, first claimant by key, the delivery chain, death and arrivals in 3-7 days, injury, buying and commissioning, retirement, grouping, the same days giving the same lives, 1,000 adventurers.
- `tests/Application.Tests/AdventurerWorldTests.cs`: the cast (4 a town, Suggest names, levels 1-5, bodies), T-22 chunk invariance over 9 days in three splittings, a running midnight against an away one, competitive claims on the boards, hiring with level and gear kept and given back, a hired adventurer's death and replacement, an LFG post answered and the ally parting at midnight.

## Persistence

Schema 29 (renumbered from 27 at the wave 4 integration, after the soak's 27 and WP12b's 28): the entities section gains `adventurers` (the last day lived, every adventurer, the slots awaiting arrivals), and NPC life's vacancies gain `arriving_name` (a retiring adventurer promised the slot). Null before adventurers first lived. Fixture v29; `tests/Application.Tests/AdventurerSaveTests.cs` saves and loads the region with one adventurer in the character's hire and checks the world goes on the same. A body left standing in sight while its adventurer was out is not saved: a load puts each where their life has them.

## Open questions

1. **WP12b's hireability (PR #22).** `src/World/Runtime/NpcLifeContracts.cs` holds word-for-word copies of WP12b's `HireabilityChanged`, `NpcHireView` and `INpcHireability`; delete it when #22 merges. Adventurers answer `INpcHireability` themselves (hireable in town, on their feet, with a body, not with the party), raise `HireabilityChanged` on each change, and are read by `Simulation.AdventurerHireability`. Their hire roles stay in `config.adventurers` (`hire`), not WP12b's `roles:`. Whether the integrated simulation should answer one `IsHireable` for residents and adventurers alike is for the integrator. *Integrated (wave 4):* the stand-in is deleted; `Simulation.Hiring` answers residents and adventurers alike, and the party's hire asks the adventurers too.
2. **WP12b's hired state.** Residents are suspended through `HandToParty` / `TakeBackFromParty`; adventurers have their own (`Party` on the record, through `AdventurerPartyChanged`). WP12b's `NpcPartyLife` will also dispatch `HandToParty` for an adventurer key, which `NpcSystem` should refuse quietly, as for anyone with no routine. *Integrated:* `NpcSystem` refuses it quietly (no life here).
3. **Trips.** WP12b gives the LFG `setup.SettingOuts ?? _npcs` and reads them as `Simulation.Trips`; this branch gives it `new CombinedSettingOuts(setup.SettingOuts, _adventurers)`. On rebase: `new CombinedSettingOuts(setup.SettingOuts ?? _npcs, _adventurers)`, and `Trips` may want the same join. *Integrated:* as proposed; `Simulation.Trips` reads the same join.
4. **Tier-A hunting alone.** An adventurer fights for real at tier A only when in the party (as a hireling or an ally, through the companion combat behaviour). One out on their own is never in sight: their day is resolved by the table, and their body is away from the lodge. A visible walk to the grounds needs WP12's mover and presentation.
5. **A hired adventurer's death while the character is away** comes from the party's advance, which runs after the adventurers'. The slot's arrival is counted from the day they fell, but a split absence may bring the arrival a day earlier than an unsplit one. Fixing it means ordering the party before the creatures, or the arrival closed-form. *Fixed at integration:* the party now advances before the adventurers, with the party's tick, so any split agrees (test included).
6. **Renamed content.** Adventurers' gear, pack and settlements are matched against content when the world starts; a renamed item is not carried over by the definition-ID pass (the NPC lives are treated the same way).
7. **Numbers.** Every value in `config.adventurers` (target, outcome rows, prices, retirement at 600 coin or 180 days, LFG chance) is provisional, for the owner to tune.
