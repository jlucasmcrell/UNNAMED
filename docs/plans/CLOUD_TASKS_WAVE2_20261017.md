# Cloud Claude: wave 2 (systems completeness)

Wave 1 is integrated (snapshot and cloud base at 963b9793, schema v19); these can start now.

- **Repo:** `jlucasmcrell/otherreach-RESTORE-20261014`
- **Base branch:** `cloud/base-20261017`
- All five can run at the same time.

Every prompt below starts with the same shared header, so paste each prompt whole.

---

## 1. WP14: living economy

> Repo jlucasmcrell/otherreach-RESTORE-20261014, base branch cloud/base-20261017.
>
> Read first:
> - AGENTS.md
> - docs/plans/SYSTEMS_COMPLETENESS_PLAN_20261017.md, in full for §5.2 and the §6 header rules, and especially §2 (living economy) and the WP14 brief in §6
> - docs/plans/README.md
>
> Owner rules (MMO rules):
> - No pause.
> - No difficulty presets.
> - No quickload.
> - The world keeps living offline.
> - No network requests.
> - Never read the wall clock outside Application/GameSession.cs.
> - No new lore.
>
> Persistence: put all Persistence edits in ONE final commit, using the next free schema version after the current SaveModel.SchemaVersion. The integrator renumbers if needed. Every new world system implements IAbstractAdvance with a chunk-invariance test.
>
> Implement WP14 exactly as briefed:
> - Town markets with daily supply and demand flows.
> - Bounded prices (DN6: 0.6–1.6× base, configurable in content).
> - Merchant purses and restock.
> - Sell history.
> - The road trade link.
> - Offline catch-up via IAbstractAdvance.
> - The 100-run exploit harness, if it's in the brief.
>
> You own World/Runtime/Trade.cs and the new World/Runtime/Merchants.cs. Build, all tests and the content lint must pass. Open a PR into cloud/base-20261017, listing open questions.

## 2. WP6: factions and reputation

> Repo jlucasmcrell/otherreach-RESTORE-20261014, base branch cloud/base-20261017.
>
> Read first:
> - AGENTS.md
> - docs/plans/SYSTEMS_COMPLETENESS_PLAN_20261017.md, in full for §5.2 and the §6 header rules, and especially §2.7 and the WP6 brief in §6
> - docs/plans/README.md
>
> Owner rules (MMO rules):
> - No pause.
> - No difficulty presets.
> - No quickload.
> - The world keeps living offline.
> - No network requests.
> - Never read the wall clock outside Application/GameSession.cs.
> - No new lore.
>
> Persistence: put all Persistence edits in ONE final commit, using the next free schema version after the current SaveModel.SchemaVersion. The integrator renumbers if needed. Every new world system implements IAbstractAdvance with a chunk-invariance test.
>
> Implement WP6:
> - Re-port the faction slice from the branch `origin/claude/m7-factions-building` on jlucasmcrell/UNNAMED. It's a different repo, so fetch it as a second remote and port by hand; don't merge it.
> - Rename the factions to the canon Wayhold and Long Survey chapters.
> - Standing is report-only.
> - Faction reward, objectives and a `standing` condition (added after WP5's condition parser, which is now merged).
> - A ReputationPanel. A simple panel is fine; the local lane polishes it.
> - Faction IAbstractAdvance is a no-op, with a test.
>
> Build, all tests and the content lint must pass. Open a PR into cloud/base-20261017.

## 3. WP7: calendar and weather (cloud part)

> Repo jlucasmcrell/otherreach-RESTORE-20261014, base branch cloud/base-20261017.
>
> Read first:
> - AGENTS.md
> - docs/plans/SYSTEMS_COMPLETENESS_PLAN_20261017.md, in full for §5.2 and the §6 header rules, and especially §2.8 and the WP7 brief in §6
> - docs/plans/README.md
>
> Owner rules (MMO rules):
> - No pause.
> - No difficulty presets.
> - No quickload.
> - The world keeps living offline.
> - No network requests.
> - Never read the wall clock outside Application/GameSession.cs.
> - No new lore.
>
> Persistence: put all Persistence edits in ONE final commit, using the next free schema version after the current SaveModel.SchemaVersion. The integrator renumbers if needed. Every new world system implements IAbstractAdvance with a chunk-invariance test.
>
> Implement WP7's cloud part:
> - **Calendar:** a pure function of the tick, with numbered months and tallies, since the owner chose numbers for now.
> - **WeatherSystem:**
>   - Weather is a pure function of (seed, region, game-hour index) through RngChannel.
>   - States: fair, overcast, rain, fog, wind, plus a reserved fold state.
>   - Fronts last 4–12 game hours.
>   - It is saved as a seed cursor only.
>   - It raises WeatherChanged.
>   - Its IAbstractAdvance is closed-form.
> - Per DN8 (accepted), remove the world-time advance from Waystone travel in World/Runtime/Travel.cs and update its test. This is the one named exception to the travel rule.
>
> Don't change SkyView or the visuals; the local lane does that. Build, all tests and the content lint must pass. Open a PR into cloud/base-20261017.

## 4. WP15a: party core and hirelings

> Repo jlucasmcrell/otherreach-RESTORE-20261014, base branch cloud/base-20261017.
>
> Read first:
> - AGENTS.md
> - docs/plans/SYSTEMS_COMPLETENESS_PLAN_20261017.md, in full for §5.2 and the §6 header rules, and especially the companions/hirelings section in §2 and the WP15 brief (part a) in §6
> - docs/plans/README.md
>
> Owner rules (MMO rules):
> - No pause.
> - No difficulty presets.
> - No quickload.
> - The world keeps living offline.
> - No network requests.
> - Never read the wall clock outside Application/GameSession.cs.
> - No new lore.
>
> Persistence: put all Persistence edits in ONE final commit, using the next free schema version after the current SaveModel.SchemaVersion. The integrator renumbers if needed. Every new world system implements IAbstractAdvance with a chunk-invariance test.
>
> Implement WP15a:
> - **Party:** the player plus up to 3 (DN11; combat animals and summons take a slot), building on World/Runtime/Companions.cs (Tavar).
> - **Party commands:** follow, wait, stance, tactics.
> - **Hirelings:**
>   - Contracts and wages, with wage numbers in content (DN11).
>   - Roles: guard, porter, gatherer, crafter. Gatherers deliver into storage once building lands; until then, deliver to the player or to a stash.
>   - Dismissal.
>   - Death is possible for hirelings, and is stated at hire (DN9).
> - **Named companions** are gravely wounded instead of dying, and recover at home.
> - **Offline:** hirelings keep working and drawing wages while offline, via IAbstractAdvance.
>
> No UI beyond a minimal debug or command surface; the local lane builds the party UI. Build, all tests and the content lint must pass. Open a PR into cloud/base-20261017.

## 5. WP10 (cloud part): guidance, Downed, audio and crash hooks

> Repo jlucasmcrell/otherreach-RESTORE-20261014, base branch cloud/base-20261017.
>
> Read first:
> - AGENTS.md
> - docs/plans/SYSTEMS_COMPLETENESS_PLAN_20261017.md, in full for §5.2 and the §6 header rules, and especially §2.10 and the WP10 brief in §6
> - docs/plans/README.md
>
> Owner rules (MMO rules):
> - No pause.
> - No difficulty presets.
> - No quickload.
> - The world keeps living offline.
> - No network requests.
> - Never read the wall clock outside Application/GameSession.cs.
> - No new lore.
>
> Persistence: put all Persistence edits in ONE final commit, using the next free schema version after the current SaveModel.SchemaVersion. The integrator renumbers if needed. Every new world system implements IAbstractAdvance with a chunk-invariance test.
>
> Implement WP10's cloud part:
> - **Hints:** a `hint` content kind with about 20 hints, including the away sheet, hiring, party commands and price changes. It reads the guidance level and SeenHints from SettingsStore, which WP1 added.
> - **Downed state:** applies only when a companion or hireling is present (OD-13 = B), for 30 s.
> - **No difficulty or World Rules.**
> - **Crash hooks:** local-only, plus a notice at the next launch. Never send anything over the network.
> - **Audio:** the missing sound hooks, including party and hireling events. Ambience, Interface and Voice buses already exist via WP1; add `default_bus_layout.tres` if it's still missing.
>
> Only WP10 may edit World/Runtime/Combat.cs (for Downed). Build, all tests and the content lint must pass. Open a PR into cloud/base-20261017.
