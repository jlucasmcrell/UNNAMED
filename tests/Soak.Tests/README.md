# Wave 4 headless gates

.NET 8 / xUnit only. References Application and SaveTool; never boots Godot, creates a graphics device, or uses a network client. All simulation time is supplied as ticks. `Stopwatch` measures elapsed benchmark time only and never feeds game state. Seeded `Random` chooses test scripts, not simulation outcomes. No production behavior or historical fixtures are modified.

Run from the repository root:

```powershell
dotnet build src/UNNAMED.sln
dotnet test src/UNNAMED.sln
# Focused default checks with timing and diagnostic output:
dotnet test tests/Soak.Tests --logger "console;verbosity=detailed"
```

The default run executes the 183-day gate, a one-seed/two-day economy smoke, eight save checkpoints across 183 days, all historical migrations, and 600 motion ticks. Longer tests have both a `Category` trait and an environment-gated fact: categories alone do not exclude tests from ordinary `dotnet test`.

```powershell
# 100 seeds, each running ALL available economy strategies for 14 days;
# plus 128 save checkpoints over one Reckoning.
$env:OTHERREACH_SOAK = 'Soak'
dotnet test tests/Soak.Tests --filter "Category=Soak" --logger "console;verbosity=detailed"

# Three Reckonings (549 days). The 36,000-tick motion probe is a KnownBug below.
$env:OTHERREACH_SOAK = 'Long'
dotnet test tests/Soak.Tests --filter "Category=Long" --logger "console;verbosity=detailed"

# Everything available, including smoke; known findings remain skipped.
$env:OTHERREACH_SOAK = '1'
dotnet test tests/Soak.Tests --logger "console;verbosity=detailed"
Remove-Item Env:OTHERREACH_SOAK
```

On a POSIX shell use `OTHERREACH_SOAK=1 dotnet test tests/Soak.Tests ...`. Do not rebuild the same configuration while a testhost is running: Windows locks loaded DLLs.

## What the gates assert

**Offline:** one `Simulation.Advance(183 * TicksPerDay)` against 183 daily steps and seeded, unaligned random spans of 1 tick through 11 days. The 549-day version has the same comparisons. Exact `StateDump.Render` bytes and `StateDigest` must agree, along with calendar, weather and weather cursor, then another 40 active ticks must agree. A reflection inventory checks that *every* concrete `IAbstractAdvance` in the runtime assembly appears in Simulation's dispatch list. Current coverage: clock, weather, creatures/respawns, status effects, gathering/nodes, quests, factions, party/contracts and markets/purses/shipments. NPC life is absent on this base; no stand-in pretends to test it.

The fixture uses all current spawns, markets and nodes, one additional wolf beside an authored node, an effect due to expire and a seven-day hireling contract. A test-only profile makes Kera hireable, following the existing Party.Tests pattern. The wolf must actually die and respawn, the node must be harvested and renew, and the effect and contract must expire. Only the single catch-up call is timed, after fixture construction/save/load; output warns above 2 seconds but never fails for machine speed.

**Economy:** seeds 0..99 each execute town arbitrage, repeated purse draining, real iron-billet/march-spear crafting and selling, and repeatable ore delivery bounties. Each day runs repeated attempts; each strategy must succeed at least once in each seeded run. Arbitrage picks the strongest available buy/sell spread between the real Hollow and Cairnwold merchants. Merchant reserves, market definitions, daily flows, restocks, sell memory, recipes, rewards and commands use current content. For a conservative exploit test, the two merchants and stations are placed together in the fixture: transport is free and stations are always within reach. This is an upper bound on trading opportunity, not a proof of navigation between towns. Finite initial material stocks are supplied once; they are never replenished by the test.

`EconomyExploitTests.ProfitCeilingPerGameDay = 500` is a test balance alarm, not a new game rule. It bounds **each elapsed day's net player coin increase**, even counting liquidation of the free starting material stock as profit. The starting player purse is 20,000 so lack of working capital does not conceal an exploit. The ceiling is intentionally above current legitimate small bounties and below repeated extraction of both 400-coin smith reserves in one day. Output reports each seed/strategy's net and mean daily margin, and the best individual daily margin. It does not claim to bound arbitrary scripts, infinite play or inventory mark-to-market wealth.

Every attempt checks stock and price factors against **content-configured** bounds and player/merchant purse non-negativity/reserve caps. Aggregate player + merchant + hireling coin cannot increase during trade/craft commands; a delivery permits only its authored 10-coin reward. A second whole-run bound permits daily full reserve refills and all authored currency rewards: it explicitly accounts for legitimate restock/income/quest faucets instead of assuming global coin conservation.

**Saves:** two branches start from the same save. At seeded random checkpoints across 183 days they open actual trade, buy, save/load one branch and continue both. A porter contract is active early in the run and expires later. Each save/load is checked with exact dump bytes and `StateDigest`. Between independently executing branches, the existing `StateDump.Render(replayable: true)` is compared byte-for-byte: newly created item/container ULIDs intentionally differ across executions (D-04). No gameplay fields are filtered by the test. Combat and Downed have separate strict regression probes because current save policy prevents the requested equivalence (below). Migration copies each fixture v1..current to a temporary profile, uses the fixture's content identity/generator and real migration path, starts the resulting record under current game setup, then advances one day. Fixture-specific entities absent from current content reconcile normally; the committed fixtures are never edited.

**Motion:** observes player, NPCs (including party bodies), creatures and world items at each tick. Bounds use configured sprint/run maxima, creature-specific movement/charge speeds, and two millimetres for integer rounding. Positions are navigation-plane X/Z coordinates; Y follows terrain/jump rules and is not treated as horizontal locomotion. Birth/removal/new creature generations are not displacement of an existing body. Only a matching `PlayerRespawned`/`PlayerTravelled` event at the observed tick can authorize a player jump; Waystone party members must arrive within the runtime's documented 2 m arrival ring. Rejected command actors are also exercised. The long run is simulated time, not a wall-time wait or a visual/GPU observation.

## Skipped findings and integration gaps

`KnownBug` tests contain executable assertions of the stronger requested contract. To reproduce, remove that test's `Fact(Skip = ...)` locally and run its fully qualified name. They remain skipped until the owner resolves the save-policy conflict; this PR does not change game behavior.

| Test / ID | Reason |
|---|---|
| `MidCombatSave_ContinuesLikeUninterruptedCombat` / W4-SAVE-01 | Saving a player attack during Windup reloads Idle. Current StateDump deliberately omits action phase, so digest equality alone can conceal this. |
| `DownedSave_ContinuesLikeUninterruptedDowned` / W4-SAVE-02 | CaptureRecord saves Downed as already helped up; the uninterrupted branch is still Downed for its remaining 600-tick recovery window. The probe also observes divergent health and creature state after continuation. |
| `ThirtyMinutesOfCommandMotion` / W4-MOTION-03 | With a following companion and longer directional walks, seed 511 at tick 33637 moves a creature 227.32 mm against its 225 mm tick limit plus 2 mm rounding allowance. Collision separation can exceed the strict speed bound. The full 36,000-tick probe found one violation and is retained skipped, without increasing tolerance to hide it. |
| `RepairCostArbitrage_RequiresARepairCommand` (`Unavailable`) | WP3b repair/durability runtime commands are absent; no invented repair formula substitutes for the real strategy. |
| `SaveDuringLfg_RequiresGroupCommands` (`Unavailable`) | JoinGroup/LFG runtime commands have not landed on this base. |

Both save findings reflect documented existing policy, but fail the task's stronger continuation requirement. They require an owner decision, not a silent harness workaround. Portal transitions and NPC life are also absent from the supplied base; extend the event adapter/fixtures when those systems land. Unrecognized jumps are violations, not implicitly permitted portals.
