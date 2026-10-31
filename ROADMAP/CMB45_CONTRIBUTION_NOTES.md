# CMB-45 — public contribution accounting (branch `systems/contribution`)

Who did what to a boss, part by part, and what the credit is worth. Branched from `systems/boss-phases` (`93f53feb`) so it sits on the
phase work rather than beside it; the first commit is `08c8c4bf`.

## What works and is tested

- `ContributionLedger` (Domain): contributions keyed by attacker and part. Parts are the body's regions today; a part id arrives with
  CMB-39. A weak-point blow weighs double (`ContributionRules.Weigh`); a blow that did no harm counts for nothing.
- `ContributionRules.Split`: shares in per mille that always sum to exactly 1000 — largest first, ties by actor id, the rounding remainder
  to the largest contributor. The same ledger always divides the same way.
- `ContributionShare.Qualified`: five per cent is the floor for qualifying for an encounter's rewards (E4), a rule and not a
  per-encounter number.
- `ContributionRules.Purse`: an encounter's credit is paid out of a tenth of what it was worth in experience, so the purse is deterministic.
- `ContributionSystem` (World): builds a ledger from `HitResolved`, concludes it on `CreatureKilled`, publishes `EncounterConcluded` with the
  shares and the region, records the outcome and the character's qualification in world flags, and **pays** each qualifying non-character
  contributor their own per-mille through `AdventurerShare` — the same event the bounty purse split already speaks (the MMO rule). The
  character's own share stays the reward systems' business, as it was before.
- `Simulation.Contributions(key)` and `Contributions()` are the board's view.

Eight deterministic domain tests cover the rules. Two application tests cover the read surface and the ground the accounting stands on (real
blows land and the world publishes them).

## The gap, plainly — do not merge on the strength of a green suite

**No test yet proves the ledger is populated by a real fight**, and the reason is not understood well enough to call it a fixture problem.

Verified by experiment: in a real fight the world publishes `HitResolved` events with damage (a test subscription to the arena's bus sees
them), and the shared `Arena.Fight` helper does land them. But the same hits never reach `ContributionSystem.Landed` in that fixture. Three
registration strategies were tried and none fired:

1. `bus.Listen<HitResolved>(...)` in the simulation's constructor — the convention every other system in that file uses;
2. `context.Events.Listen<...>(...)` in the system's own constructor — rejected by the type (`IEventBus` has no `Listen`);
3. `bus.Subscribe<...>(...)` — the raw-bus channel.

`SimulationEvents` (`src/World/Runtime/Events.cs`) keeps its own listener list and forwards to the raw bus on `Publish`, while events
published through the context go to the raw bus directly. That is where the discrepancy almost certainly lives, and it deserves one
deliberate investigation rather than another guess: **read `Events.cs`, find how a system is meant to hear an event another system publishes
in a live run, and make this system do exactly that.** The branch keeps strategy 1, because it is what the neighbouring systems do.

Until that is settled, treat the accounting's **rules** as proven and its **wiring** as compiled-but-unproven.

## Waiting on other lanes

- **Per-part accounting is per body region until CMB-39 lands.** `ContributionParts` is the seam: when multi-part bosses and a real part
  table exist, `ContributionParts.Of` and the ledger's key gain the part id, and nothing else has to move.
- **`HitResolved` carries no weak-point flag**, so `ContributionRules.Weigh`'s multiplier currently rides on `Critical`. When CMB-38's timed
  weak-point windows report their own, the event should carry `WeakPoint` and the ledger should read that instead of the critical.
