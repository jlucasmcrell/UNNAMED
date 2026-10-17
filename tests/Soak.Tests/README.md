# Wave 4 headless gates

.NET 8 / xUnit only. References Application and SaveTool; never boots Godot, creates a graphics device, or uses a network client. All simulation time is supplied as ticks. `Stopwatch` measures elapsed benchmark time only and never feeds game state. Seeded `Random` chooses test scripts, not simulation outcomes. No production behavior or historical fixtures are modified.

Run from the repository root:

```powershell
dotnet build src/UNNAMED.sln
dotnet test src/UNNAMED.sln
# Focused default checks with timing and diagnostic output:
dotnet test tests/Soak.Tests --logger "console;verbosity=detailed"
```

The default run executes the 183-day gate, a one-seed/two-day economy smoke, eight save checkpoints across 183 days, all historical migrations, the interrupted-save probes, and 600 and 36,000 motion ticks. Longer tests have both a `Category` trait and an environment-gated fact: categories alone do not exclude tests from ordinary `dotnet test`.

```powershell
# 100 seeds, each running ALL available economy strategies for 14 days;
# plus 128 save checkpoints over one Reckoning.
$env:OTHERREACH_SOAK = 'Soak'
dotnet test tests/Soak.Tests --filter "Category=Soak" --logger "console;verbosity=detailed"

# Three Reckonings (549 days).
$env:OTHERREACH_SOAK = 'Long'
dotnet test tests/Soak.Tests --filter "Category=Long" --logger "console;verbosity=detailed"

# Everything available, including smoke.
$env:OTHERREACH_SOAK = '1'
dotnet test tests/Soak.Tests --logger "console;verbosity=detailed"
Remove-Item Env:OTHERREACH_SOAK
```

On a POSIX shell use `OTHERREACH_SOAK=1 dotnet test tests/Soak.Tests ...`. Do not rebuild the same configuration while a testhost is running: Windows locks loaded DLLs.

## What the gates assert

**Offline:** one `Simulation.Advance(183 * TicksPerDay)` against 183 daily steps and seeded, unaligned random spans of 1 tick through 11 days. The 549-day version has the same comparisons. Exact `StateDump.Render` bytes and `StateDigest` must agree, along with calendar, weather and weather cursor, then another 40 active ticks must agree. A reflection inventory checks that *every* concrete `IAbstractAdvance` in the runtime assembly appears in Simulation's dispatch list. Current coverage: clock, weather, creatures/respawns, status effects, gathering/nodes, quests, factions, party/contracts, party sheets and approval, markets/purses/shipments, teaching, durability and laden (no-ops away), LFG offers, and NPC life.

The fixture uses all current spawns, markets and nodes, one additional wolf beside an authored node, an effect due to expire and a seven-day hireling contract. A test-only profile makes Kera hireable, following the existing Party.Tests pattern. The wolf must actually die and respawn, the node must be harvested and renew, and the effect and contract must expire. Only the single catch-up call is timed, after fixture construction/save/load; output warns above 2 seconds but never fails for machine speed.

**Economy:** seeds 0..99 each execute town arbitrage, repeated purse draining, real iron-billet/march-spear crafting and selling, and repeatable ore delivery bounties. Each day runs repeated attempts; each strategy must succeed at least once in each seeded run. Arbitrage picks the strongest available buy/sell spread between the real Hollow and Cairnwold merchants. Merchant reserves, market definitions, daily flows, restocks, sell memory, recipes, rewards and commands use current content. For a conservative exploit test, the two merchants and stations are placed together in the fixture: transport is free and stations are always within reach. This is an upper bound on trading opportunity, not a proof of navigation between towns. With WP12 the merchants walk their day and trade only at work in trading hours, so Durn's routine anchors are moved a stride beside Kera's anvil too, and each day waits until the merchants it needs are at work, as the playthrough waits out a meal (`SoakWorld.AwaitTrade`). Finite initial material stocks are supplied once; they are never replenished by the test.

`EconomyExploitTests.ProfitCeilingPerGameDay = 500` is a test balance alarm, not a new game rule. It bounds **each elapsed day's net player coin increase**, even counting liquidation of the free starting material stock as profit. The starting player purse is 20,000 so lack of working capital does not conceal an exploit. The ceiling is intentionally above current legitimate small bounties and below repeated extraction of both 400-coin smith reserves in one day. Output reports each seed/strategy's net and mean daily margin, and the best individual daily margin. It does not claim to bound arbitrary scripts, infinite play or inventory mark-to-market wealth.

Every attempt checks stock and price factors against **content-configured** bounds and player/merchant purse non-negativity/reserve caps. Aggregate player + merchant + hireling coin cannot increase during trade/craft commands; a delivery permits only its authored 10-coin reward. A second whole-run bound permits daily full reserve refills and all authored currency rewards: it explicitly accounts for legitimate restock/income/quest faucets instead of assuming global coin conservation.

**Saves:** two branches start from the same save. At seeded random checkpoints across 183 days they wait for Kera to be at work, open actual trade, buy, save/load one branch and continue both. A porter contract is active early in the run and expires later. Each save/load is checked with exact dump bytes and `StateDigest`. Between independently executing branches, the existing `StateDump.Render(replayable: true)` is compared byte-for-byte: newly created item/container ULIDs intentionally differ across executions (D-04). No gameplay fields are filtered by the test. Combat and Downed have separate probes: a save mid-attack must continue like the uninterrupted fight (below), and a save while Downed loads as the death (DN15): the resumed branch dies once, owes the usual XP debt and respawns at the return point, while the uninterrupted one is helped up. Migration copies each fixture v1..current to a temporary profile, uses the fixture's content identity/generator and real migration path, starts the resulting record under current game setup, then advances one day. Fixture-specific entities absent from current content reconcile normally; the committed fixtures are never edited.

**Motion:** observes player, NPCs (including party bodies), creatures and world items at each tick. Bounds use configured sprint/run maxima, creature-specific movement/charge speeds, and two millimetres for integer rounding. Positions are navigation-plane X/Z coordinates; Y follows terrain/jump rules and is not treated as horizontal locomotion. An NPC with a WP12 life (not a companion) is bounded by the tier the tick simulated them at, as `NpcLifeWorldTests` bounds them: tier A by the tick, tier B by its ten-tick glide plus half a metre, tier C and D (out of sight, parked by the hour) not at all. Birth/removal/new creature generations are not displacement of an existing body. Only a matching `PlayerRespawned`/`PlayerTravelled` event at the observed tick can authorize a player jump; Waystone party members must arrive within the runtime's documented 2 m arrival ring. A collision's separation is a push, not motion: a body past its bound that ends touching something (a structure, closed door, creature, person or the character) is reported as a push, not a violation, if the excess is within the two footprints' radii. Free motion keeps the strict bound. The 36,000-tick run reports two: a wolf 2.3 mm past its tick's travel, off a tree (W4-MOTION-03), and the Hollow's porter pushed 0.31 m off a hen that had walked into them while they ate. Rejected command actors are also exercised. The long run is simulated time, not a wall-time wait or a visual/GPU observation.

## Findings and gaps

The three findings PR #19 reproduced are fixed, and their tests run by default: W4-SAVE-01 (the attack under way is saved, schema 27), W4-SAVE-02 (a save while Downed loads as the death, DN15) and W4-MOTION-03 (a collision push is classified as a push). Only the opt-in Soak and Long runs are skipped by default.

Not yet covered: repair-cost arbitrage (WP3b's `MerchantRepairCommand`) and a save during LFG (WP16's `JoinGroupCommand`). Both landed in wave 3; their strategies are still to write.

Portal transitions are absent from the supplied base; extend the event adapter/fixtures when they land. Unrecognized jumps are violations, not implicitly permitted portals.
