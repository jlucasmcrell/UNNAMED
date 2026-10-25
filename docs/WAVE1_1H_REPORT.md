# Wave 1 WP 1H: World Event Director and history - report

Branch `claude/wave1-1H`, cut from `playtest/humgen-main` at `e38fcdf4` as briefed. The base branch has since moved four commits on
(to `117f1b8a`); this branch has not been rebased onto them. Pushed only: no merge, no PR.

## What was built

**The Director framework** (`src/World/Runtime/Director.cs`):

- **Scheduler (QST-55).** A seeded, controlled-random scheduler. It settles one game hour at a time, on the world clock's absolute hours. Each hour:
  - Events whose time is up end. Each end gets a seeded, weighted outcome and goes into the history.
  - Each kind of event, in content order, may then begin, provided that:
    - its region is under the cap;
    - the world is old enough (`min_world_day`);
    - no event of its kind is running or began within its cooldown;
    - a roll falls under its hourly odds, after recent intensity has damped them.
  - Every roll is an `RngChannel` keyed on the world seed, the region, the kind and the hour, never on call order.
  - Event IDs (`evt_`) are derived from the roll, so the same event has the same ID however its hour was reached.
- **Cap per region (WLD-89).** `max_active_per_region` is at most 8 (RK-A9). Content cannot raise it; DIR001 refuses 9.
- **History record (QST-52/55).** `DirectorState.History` holds the resolved events: ID, kind, region, start and end hour, and outcome. It keeps the last `history_kept` (64). The game reads it as `Simulation.WorldHistory`; the running events are `Simulation.WorldEvents`.
- **Notification channels (QST-48).** Each begin and each end publishes one `WorldEventNotice` that names the event's channels. The known channels are `away_log`, `notice_board`, `chat`, `crier`, `rumour`, `letter` and `faction_intelligence`; content naming any other is refused (DIR001). During an absence the director writes the `away_log` notices into the advance log itself.
- **Event scales (QST-56).** Personal, local, regional, continental and cosmological. Each event carries its scale; nothing reads it yet.
- **Offline catch-up equals live.** `DirectorSystem.Tick` (live) and `DirectorSystem.Advance` (catch-up) both call the same `SettleTo(hour)`. An absence caught up at once, in pieces, or lived tick by tick therefore settles the same hours with the same rolls. It ends with the same state and publishes the same notices.
- **The `OfflineCatchUp.cs` hook.** `OfflineWorldEvents.From(lines)` folds an absence's director lines into one `AwayWorldEvent` per event: when it began (null if before the absence), and when and how it ended (null if it is still running). This is what an away sheet lists.

**Proof events** (`content/config/director.yaml`, built and linted by `src/Content/DirectorContent.cs`). Both are resolved abstractly:

- `event.travelling_trader`
  - Scale: local.
  - From day 1, at 40‰ per hour, for 6 to 12 h, with a 24 h cooldown.
  - Announced on the notice board, chat and the away log.
  - Ends `moved_on` (weight 3) or `sold_out` (weight 1).
- `event.creature_migration`
  - Scale: regional.
  - From day 3, at 10‰ per hour, for 24 to 48 h, with a 72 h cooldown.
  - Announced as rumour and on the away log.
  - Ends `passed_through` (weight 2) or `lingered` (weight 1).

Intensity window: 48 h. Damping: 250‰ per recent event.

**Save (one final commit).**

- What is saved: the entities section gains `director` (the settled hour, the running events and the history).
- Migration: the 39 → 40 step writes none; the director starts at the hour the world is next loaded, with nothing back-filled.
- Frozen shape: the schema 38-39 entities envelope is frozen as `Sections/V39`, and the 37 → 38 step now writes that frozen shape.
- Fixture `v40/`: a trader on the road and a migration that passed through.
- The v1-v39 `expected.json` files gain only `"director": null`, and their fixture bytes are unchanged.
- **Schema number: built as 40 here, to renumber to 45 at merge.** Schema 45 cannot be used on this branch alone: the base is at 39, and the chain and fixture tests (`TheProductionChain_HasOneStepPerVersion_InOrder`, `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture`) refuse a gap. So the step is 39 → 40, isolated in the last commit, as the coordinator ruled for WP11 ("renumber at merge time"). At merge, these change:
  - `SaveFormat.SchemaVersion`;
  - the `SchemaV39ToV40` step class, re-cut onto 44 → 45 after the steps for 40 to 44 land;
  - the `Sections/V39` freeze, re-cut on whatever entities shape 44 has;
  - the fixture directory name, regenerated with `M2.Probe fixture`;
  - the README rows;
  - the expected files, regenerated with `UNNAMED_WRITE_FIXTURE_EXPECTATIONS=1`.

## Seams outside the owned files

These are small, one-line or one-block changes:

- `RuntimeState`: the `StateSlice.Director` slice and `SetDirector`.
- `WorldDelta`: the field, the snapshot property and the load validation.
- `SimulationSetup.Director`.
- `Simulation`: construction, `Populate`, `Tick`, registration in `_advances`, the two views, and the `StateDigest` term.
- `GameSession.Boot`: one line.
- `ContentLoader`: DIR validation is registered.
- `AdvanceLine`: an `Outcome` init property, for the away log.
- Persistence: `SectionCodec`, `SaveLoader`, `SaveModel`, `Migrations` and `Sections/V39`. The brief says the WP adds its own section codec and migration step, but these files are in WP-1C's area.

## Tests and gates

- `DirectorTests` (11), in Application.Tests:
  - **Chunk invariance:** 183 days settled at once, hourly, or in random pieces give the same history and the same notices.
  - **Live equals offline in a running simulation:** six game hours stepped tick by tick against `Advance`, event for event.
  - The seed decides the world.
  - The cap of 8 holds with 12 always-on kinds.
  - Cooldown, minimum world age, intensity damping, the history kept, and the seeded outcome on each end with its channels.
  - The game's own director: capped at 8, with two proof events; the lint refuses an unknown channel and a cap of 9.
  - A save keeps the director, and the world goes on as the unsaved one does.
  - `OfflineWorldEvents.From`.
- `OfflineAdvanceTests` T-22 now also asserts that one Reckoning at once and 183 one-day advances give the same `WorldHistory` and `WorldEvents`, and that the away log tells of a travelling trader.
- The Soak offline gate (`OfflineSoakTests.Reckoning_IsByteIdentical_WholeDailyAndMixed`) now also asserts the same history whole, daily and in random chunks. Its coverage check lists `DirectorSystem`. The 183-day single advance took 815 ms in the run measured (budget 2 s).
- `HistoricalFixtureTests` assert the schema-40 director and none before it.
- Full Release suite, run project by project on this branch:

  | Project | Passed | Skipped |
  |---|---|---|
  | EntityRegistry | 23 | 0 |
  | Domain | 424 | 0 |
  | Architecture | 23 | 0 |
  | World | 90 | 0 |
  | Presentation | 434 | 0 |
  | Persistence | 280 | 1 |
  | Content | 361 | 0 |
  | Soak | 10 | 3 |
  | Application | 687 | 0 |

  The skips are opt-in Soak and Long runs, plus one Persistence skip that was already on the base. A one-shot `dotnet test` of the whole solution passed the one-hour limit on this 4-core container (Application alone took 53 min, Content 15 min), so the projects were run one at a time.
- Four failures in the first Release run were fixed and folded into the first commit:
  - **Architecture:** `WorldDelta`'s public allow-list gains `Director`.
  - **Architecture:** a notice is now published as `Publish(new WorldEventNotice(...))`; the engine reports a plain `WorldEventHappening`.
  - **Content:** `LoadAll_Loads_Yaml_Files` gains `config.director`.
  - **Application:** the M6 acceptance save's list of fields added since schema 12 gains the director, and its count is raised by one. Only `GameSaveTests` was rerun after this fix, not the whole Application project.
- Content lint (Release): 0 errors. xref: exit 0.

## Visible to film

Nothing yet. The director publishes notices, but no presentation code consumes them in this WP, and the away sheet does not yet list `OfflineWorldEvents`. Nothing appears on screen to film. A route video becomes possible once a consumer exists, for example a notice-board card, a chat line, or the away sheet's world-events lines.

## Row status after this WP

| Row | Status | What is left |
|---|---|---|
| QST-55 | PARTIAL | Inputs used: world age, recent intensity, cooldown, the seed and the cap. Population, faction and cosmic inputs and story seeds are not read. |
| QST-56 | PARTIAL | Scale is data on every event. No sky, religion or market consequence. |
| QST-48 | PARTIAL | Channel registry, notices and the away log. Board, chat, crier, rumour, letter and faction-intelligence consumers are not wired. |
| QST-57, QST-58 | MISSING | No contribution ledger and no persistent epic arcs. The director's per-event state is where they would attach. |
| WLD-89 | PARTIAL | Cap of 8 per region and abstract resolution, proven with a caravan and a migration. Incursions, sieges and festivals are content, and world consequences are for the economy and NPC-life waves. |

## Open questions

1. Should the director back-fill from world start when an old save is first loaded, or begin at load as now? Now is cheaper and honest about history.
2. How should several regions be handled? A simulation runs one layout (`Layout.Id`), and every event is keyed and capped by that region. Should events in regions the character is not in be settled too?
3. Notice wording is not written, because chat and board lines are content. Which channel consumer comes first, and which WP owns its text?
4. Should outcomes change the world through events (for example a market stock change, or a spawn table shift), or through commands sent to their owners? The architecture rule says commands.
5. An existing behaviour noticed in passing: an arena-built world that has been advanced, saved and loaded does not reproduce its whole `StateDigest`, even with the director empty. It is outside this WP, so the director's save test compares the director's own state only.

## Commits

1. `Add the World Event Director: seeded events, the world's history, channels (WP 1H)`
2. This report.
3. `Save the World Event Director: schema 40 (WP 1H, to renumber 45 at merge)`, the one Persistence commit, last.
