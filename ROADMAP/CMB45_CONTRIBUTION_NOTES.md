# CMB-45 — public contribution accounting (branch `systems/contribution`)

Who did what to a boss, part by part, and what the credit is worth. Branched from `systems/boss-phases` (`93f53feb`); first commit `08c8c4bf`.

## What works and is tested

- `ContributionLedger` (Domain): contributions keyed by attacker and part. Parts are the body's regions today; a part id arrives with CMB-39.
  A weak-point blow weighs double (`ContributionRules.Weigh`); a blow that did no harm counts for nothing.
- `ContributionRules.Split`: shares in per mille that always sum to exactly 1000 — largest first, ties by actor id, the rounding remainder to
  the largest contributor. The same ledger always divides the same way.
- `ContributionShare.Qualified`: five per cent is the floor for qualifying for an encounter's rewards (E4), a rule and not a per-encounter
  number.
- `ContributionRules.Purse`: an encounter's credit is paid out of a tenth of what it was worth in experience, so the purse is deterministic.
- `ContributionSystem` (World): builds a ledger per encounter, concludes it when the creature falls, publishes `EncounterConcluded` with the
  shares and the region, records the outcome and the character's qualification in world flags, and **pays** every qualifying non-character
  contributor their own per-mille through `AdventurerShare` — the event the bounty purse split already speaks (the MMO rule). The character's
  own share stays with the reward systems that own a character's purse.
- `Simulation.Contributions(key)` and `Contributions()` are the board's view.

**Tests:** eight deterministic domain tests over the rules, plus two application tests. One of them fights for real — the shared `Arena.Fight`
helper, an armed character, an actual kill — and asserts the ledger is private before the first blow, that the encounter concludes on the death,
that the shares are the character's whole contribution, that it qualified, that the outcome is qualified by the region, and that the ledger is
spent once the encounter is over.

## How a live system hears about a fight — the thing that cost this lane a day

Events published through `SystemContext` go to the **raw bus**, while `SimulationEvents` keeps its own listener list and only runs it when
*it* is the publisher. So a system that wants to react to something another system does should not rely on `Listen`: it should have the other
system **dispatch an internal command**, exactly as `Die` dispatches `LeaveCarcassCommand` and the carcass system handles it.

That is what this branch does: `Creatures.Handle(WoundCreature)` dispatches `RecordContribution`, `Die` dispatches `ConcludeEncounter`, and
`Simulation` routes both to `ContributionSystem`. Commands are the guaranteed channel; events are for views.

## Waiting on other lanes

- **Per-part accounting is per body region until CMB-39 lands.** `ContributionParts` is the seam: when multi-part bosses and a real part table
  exist, `ContributionParts.Of` and the ledger's key gain the part id, and nothing else has to move.
- **`HitResolved` carries no weak-point flag**, so `ContributionRules.Weigh`'s multiplier currently rides on `Critical`. When CMB-38's timed
  weak-point windows report their own, the event (and `RecordContribution`) should carry `WeakPoint` and the ledger should read that instead.
