# M7 research notes: persistence (key = persistence)

Scope: map the save system precisely enough to specify **schema 14** for M7 (Factions, Reputation, Building v1).
Canonical source: read-only snapshot of `origin/main` at `e10d2c4`. Every `path:line` citation is repo-relative and valid at `e10d2c4`.
Git history was read (read-only `git log -S` / `git show`) to find the commits that bumped schema 11, 12 and 13.
Labels: **FACT** = cited from source; **INFERENCE** = my reading or a design implication. Untracked docs are marked "untracked, authority unconfirmed".

---

## 0. Summary of what matters most for schema 14

1. **The save is 4 files plus an integrity root, not the 10-file layout PERSISTENCE.md §3.2 describes.** FACT: `SaveFormat` defines only `manifest.json`, `player.msgpack`, `cells.msgpack`, `entities.msgpack`, plus `sections.sha256` (`src/Persistence/SaveModel.cs:24-32`). `buildings.msgpack`, `companions.msgpack`, `journal.jsonl`, `command_log.jsonl` and `orphans.msgpack` were never built. M2 deferred them "with the systems that own them" (`docs/M2_STATUS.md:83`).
2. **Adding a new section *file* is a trap under the current integrity code.** FACT: `SaveIntegrity.TryReadRoot` returns null unless every name in `SaveFormat.CheckedFiles` appears in `sections.sha256` (`src/Persistence/SaveLoader.cs:469`). A null root sets `IntegrityRootRederived` (`SaveLoader.cs:70-71`), and that makes `LoadResult.IsComplete` false (`SaveModel.cs:131-132`). Two things then break: `SaveStore.Migrate` refuses (`SaveStore.cs:260-263`), and `Fixture_LoadsToItsExpectedCurrentState` fails, because it asserts `IsComplete` (`tests/Persistence.Tests/HistoricalFixtureTests.cs:105`). INFERENCE: if `buildings.msgpack` is simply added to `CheckedFiles`, all 13 historical fixtures and every existing player save stop loading cleanly. A new file needs a per-schema expected-file set. The alternative is the pattern every world extension so far has used: a new list inside `entities.msgpack` (containers arrived at schema 6, creatures at schema 8).
3. **Every world record is anchored to a host cell and proven by that cell's `baseline_hash`.** FACT: `WorldDelta.FromSnapshot` rejects any record whose hash does not match (`src/World/WorldDelta.cs:681-682`, `724-726`, `748-749`, `771-772`, `800-801`), and `SaveLoader.ProveBaselines` checks every host cell (`SaveLoader.cs:380-389`). **There is no global (non-cell) world state in the save today**: world flags are per cell (`WorldDelta.cs:250-259`). INFERENCE: faction-to-faction relations and war state have no existing home. They need either a new world-global record (new shape, no baseline proof) or the player section.
4. **The authored region layout (structures, doors, blockers, NPC sites) is not part of the baseline hash.** FACT: `CellBaseline.Digest` covers only the terrain hash, nodes and populations (`src/World/Generation.cs:144-162`). The worldgen fingerprint covers the generation profile (including authored fixed nodes) and probe output (`Generation.cs:101-120`, `306-319`). The region's structures, doors, switches, barriers and NPC sites live in `RegionLayout` (`src/Domain/Spatial/RegionLayout.cs:60-98`), which is runtime content. INFERENCE: a player structure placed next to authored geometry gets no baseline-hash protection if that geometry later changes. M2b deferred "position legality after a rebase" (`docs/M2B_STATUS.md:135`).
5. **Known pitfall:** two places rebuild a `DeltaSnapshot` by listing its init properties by hand:
   - the definition-ID pass (`src/Persistence/SaveLoader.cs:356-358`);
   - the semantic rebase (`src/Persistence/BaselineTransitions.cs:112-117`).

   INFERENCE: a new `DeltaSnapshot` property left out of either one is silently emptied whenever content changes (every content update) or a transition applies. Tests should load a v14 fixture under a changed content hash; the historical fixtures already do this, because their writer pack is 0.1.6 and they load against 0.2.8.
6. **The 415-field comparison is reflection-driven and picks new fields up automatically, but only inside `PlayerRecord` and `DeltaSnapshot`.** FACT: `StateDump.Render` serializes `world_tick`, `simulation.CaptureRecord()` (a `PlayerRecord`) and `simulation.World.TakeSnapshot()` (a `DeltaSnapshot`) with System.Text.Json (`src/Application/StateDump.cs:25-46`). INFERENCE: faction or structure state kept anywhere else is invisible to the save/relaunch comparison unless `Render` adds a root key. The historical-fixture comparison (`CanonicalState`) and the state digest (`EffectiveCellDigest`, `PlayerRecord.Digest`) are **hand-written**. Every new field must be added to them explicitly.
7. **Load-time rebuild point for derived data (such as navigation):** `Simulation`'s constructor (`src/World/Runtime/Simulation.cs:100-145`) runs after the whole persistence pipeline and on both new-game and load paths. The order inside it matters: `_npcs.Populate()`, `_companions.Populate()`, `_creatures.Populate()` (which calls `Kinematics.IsClear` against `Layout.Space`, `src/World/Runtime/Creatures.cs:171-195`), then `_tiers.Settle()`. INFERENCE: navigation or collision derived from structures must be built before the Populate calls.
8. **Doctrine a schema-14 change must obey:**
   - one ordered step `SchemaV13ToV14`;
   - frozen previous shapes;
   - a new `v14/` fixture written by `M2.Probe`, with no older fixture byte edited;
   - every `expected.json` regenerated with `UNNAMED_WRITE_FIXTURE_EXPECTATIONS=1` and reviewed line by line;
   - new required fields nullable in the DTO but "corrupt, not defaulted" on decode;
   - the definition-ID pass extended to every new stored definition ID;
   - baseline proof extended to every new host-cell record;
   - never silently fall back to a backup;
   - never bump `save_format`.

---

## 1. The container as implemented

### 1.1 Files and constants (FACT)
- `SaveFormat.Current = 1` is the container version. A mismatch refuses: "a layout cannot be guessed" (`src/Persistence/SaveModel.cs:13-14`, `SaveLoader.cs:56-59`).
- `SaveFormat.SchemaVersion = 13` (`SaveModel.cs:20`) and `OldestSupportedSchema = 1` (`SaveModel.cs:22`).
- Files: `manifest.json`, `player.msgpack`, `cells.msgpack`, `entities.msgpack`, `sections.sha256` (`SaveModel.cs:24-28`). `CheckedFiles` is the ordered list `Cells, Entities, Manifest, Player` (`SaveModel.cs:31-32`).
- Slot directory names:
  - `quick`, `manual_<slug>` and `auto_01..auto_05` (`src/Persistence/SaveStore.cs:15-36`);
  - backups `.bak-<slot>.1` and `.bak-<slot>.2` (`SaveStore.cs:93`, `103-104`);
  - pre-migration copies `pre_migration_<schema>_<slot>` (`SaveStore.cs:275-276`);
  - staging and trash leftovers `.staging-<slot>-<ULID>` and `.trash-<slot>-<ULID>`, plus `.failed-<slot>-<ULID>` (`SaveStore.cs:141-143`, `179`);
  - `rotation.json`, which holds load-proof digests (`SaveStore.cs:67`, `525-555`).
- The autosave cadence is 300 s of playtime (`SaveStore.cs:41`). `GameSession.Frame` triggers it (`src/Application/GameSession.cs:193-199`).

### 1.2 Manifest (`SaveManifest`, FACT, `src/Persistence/SaveModel.cs:46-78`)
JSON keys:
- `save_format` and `schema_version`;
- `content_version`: a label, "never a compatibility authority";
- `content_hash`;
- `world_seed`: a hex string;
- `worldgen_version`, `worldgen_fingerprint` and `rng_contract_version`;
- `world_tick` and `world_time_advanced_ticks`;
- `command_log_sha256`: optional, omitted when null;
- `build_timestamp` and `playtime_seconds`;
- `flags.quarantined_sections`.

`BuildManifest` always writes `CommandLogSha256 = null` (`SaveStore.cs:474`). No command log exists. The v13 fixture manifest shows the exact on-disk form: `tests/Persistence.Tests/Fixtures/v13/quick/manifest.json` (`schema_version` 13, `content_version` "0.1.6", `worldgen_version` 2, `rng_contract_version` 2, `world_tick` 5000).

### 1.3 Encoding (FACT)
- The sections are **string-keyed MessagePack maps**, "so a dump is self-describing" (`src/Persistence/SectionCodec.cs:17-18`).
- They are read with `MessagePackSecurity.UntrustedData` (`SectionCodec.cs:313-314`). The manifest is indented System.Text.Json (`SectionCodec.cs:316`).
- The output is "byte-stable: equal input, equal bytes (T-03)" (`SectionCodec.cs:309`). Collections are pre-sorted by the domain records (for example `PlayerRecord` sorts inventory, discoveries and relationships in its constructor, `src/World/PlayerState.cs:110`, `124`, `154-155`).
- Enums are stored as snake_case string keys, never ordinals: `StanceKeys` (`PlayerState.cs:372-387`), `CreatureConditions` and `CreatureMinds` (`WorldDelta.cs:111-152`), `QuestKeys` and `CompanionKeys` (used at `SectionCodec.cs:360`, `373-374`). The progression record follows the same rule (`src/Persistence/Sections/ProgressionSection.cs:10-11`).
- `SectionCodec.MessagePackOptions` is `internal` and shared with the migration chain (`SectionCodec.cs:319`).

### 1.4 Write sequence (FACT, `SaveStore.Commit`, `SaveStore.cs:139-190`; normative `docs/PERSISTENCE.md:421-446`)
1. Serialize every section into memory (a `SortedDictionary` of file name to bytes; `SaveStore.cs:147-153`).
2. Write to `.staging-<slot>-<ULID>` with `FileOptions.WriteThrough` plus `Flush(true)` (`SaveStore.cs:156-159`, `409-414`).
3. Write `sections.sha256`, hashed from the bytes on disk. The format is one `<sha256>  <file>` line per checked file, the sha256sum layout (`SaveLoader.cs:449-455`).
4. Move the old slot to trash, then promote staging (`SaveStore.cs:166-173`). "Never delete first".
5. Verify by re-reading and re-hashing. On failure, move the new save to `.failed-*` and restore the previous one (`SaveStore.cs:176-184`).
6. Retire the previous save (`SaveStore.cs:187-189`):
   - to `pre_migration_<schema>_<slot>` if its schema is older (`SaveStore.cs:296-300`);
   - else into `.bak-*.1` if `rotation.json` proves it was loaded cleanly (`SaveStore.cs:302-310`);
   - else delete it (`SaveStore.cs:313`).

Moves retry up to 10 attempts with backoff `min(10<<n, 2000)` ms on `IOException` (`SaveStore.cs:422-442`). `RecoverInterruptedCommits` (the boot sweep) runs whenever a `GameSession` is constructed (`GameSession.cs:72`, `SaveStore.cs:338-405`).

---

## 2. Schema history, v1 to v13

The migration table is `SchemaMigrations.Production` (`src/Persistence/Migrations.cs:69-81`). Each step is a `SchemaMigration` with `From` and `To => From + 1` (`Migrations.cs:55-64`) and a method `Apply(MigrationDocument, MigrationEnvironment, MigrationReport)`. Every step ends by setting `document.Manifest["schema_version"] = To` and appending its `Summary` to `report.Steps`.

| Ver | Milestone / commit | What it added | Sections changed | Step class (lines) | Frozen shape the step reads, and what it writes | Default given to older saves |
|---|---|---|---|---|---|---|
| 1 | M2 (`7ff4c57`) | Base format. Manifest carried `worldgen_digest`; node keys `node.<cell>.<index>` | all | - | `Sections/V1` (`SchemaV1.cs`) | - |
| 2 | M2b (`b310979`) | Worldgen 1 to 2 (semantic RNG channels); per-cell `baseline_hash`; entities-section `baselines` table; manifest `worldgen_fingerprint` and `rng_contract_version` replace `worldgen_digest` | manifest, cells, entities | `SchemaV1ToV2` (`Migrations.cs:110-250`) | reads V1, writes V2 | Regenerates worldgen 1 from the frozen `CellBaselineGeneratorV1` after checking `worldgen_digest` (`Migrations.cs:133-140`). Re-keys nodes by rule name and ordinal (`Migrations.cs:184`). Drops targets with no worldgen-2 counterpart as `report.Loss` (`Migrations.cs:179-189`, `226-229`). **The only step that uses `MigrationEnvironment.Generator`.** |
| 3 | M2b | Player `appearance_seed` (required); entities-section `created` list | player, entities | `SchemaV2ToV3` (`Migrations.cs:258-304`) | reads V2, writes V3 | `PlayerRecord.DerivedAppearanceSeed(ulid)` (`Migrations.cs:279`, `PlayerState.cs:215-219`); `Created = []` |
| 4 | M2c | Player `progression` record | player | `SchemaV3ToV4` (`Migrations.cs:312-341`) | reads V3.Player, writes V4.Player | `CharacterProgression.Empty` (`Migrations.cs:335`) |
| 5 | M3 | Player `facing_mdeg` and `discoveries` | player | `SchemaV4ToV5` (`Migrations.cs:348-378`) | reads V4, writes V5 | facing 0 (+Z); no discoveries |
| 6 | M3b | Player `equipment` and `currency`; created `count`; entities-section `containers` | player, entities | `SchemaV5ToV6` (`Migrations.cs:385-441`) | reads V5.Player and **V3.EntitiesSection**, writes V6 | no equipment, 0 coin, count 1, no containers |
| 7 | M3c | Player `effects` | player | `SchemaV6ToV7` (`Migrations.cs:447-481`) | reads V6.Player, writes **V8.Player** (the shape written by schemas 7 and 8) | no effects |
| 8 | M3d | Entities-section `creatures` | entities | `SchemaV7ToV8` (`Migrations.cs:487-528`) | reads V6.EntitiesSection, writes V8.EntitiesSection | no creature records |
| 9 | M3f | `quality` on every stack: inventory, created, container items | player, entities | `SchemaV8ToV9` (`Migrations.cs:534-587`) | reads V8, writes V9.Player and the **current** `EntitiesSectionDto` (`Migrations.cs:566`) | quality 0 (standard) |
| 10 | M4 | Player `relationships` and `conversations` | player | `SchemaV9ToV10` (`Migrations.cs:593-627`) | reads V9.Player, writes V10.Player | none |
| 11 | M5 (`0eb6247`) | Player `quests` | player | `SchemaV10ToV11` (`Migrations.cs:633-668`) | reads V10, writes V11 | none |
| 12 | M6 part 3 (`f3a064e`) | Player `companions` | player | `SchemaV11ToV12` (`Migrations.cs:671-707`) | reads V11, writes V12 | none |
| 13 | M6 owner-playtest delta (`232049e`) | Player `posture` (`stance`, `airborne`, `air_ms`) | player | `SchemaV12ToV13` (`Migrations.cs:710-747`) | reads V12.Player, writes the current `PlayerDto` | `{stance: "standing", airborne: false, air_ms: 0}` (`Migrations.cs:741`) |

Frozen shape files (FACT): `src/Persistence/Sections/SchemaV1.cs`, `V2`, `V3`, `V4`, `V5`, `V6`, `V8`, `V9`, `V10`, `V11` and `V12`.
- There is **no `SchemaV7.cs`**. The shape schemas 7 and 8 wrote is frozen as `V8.Player` (`SchemaV8.cs:4-5`).
- There is **no `SchemaV13.cs`**: the current `PlayerDto` is the schema-13 shape.
- The **cells** section shape has not changed since schema 2. Current `CellDto` (`SectionCodec.cs:169-178`) equals `V2.Cell` (`SchemaV2.cs:37-46`).
- The **entities** section shape was last changed at schema 9. Current `EntitiesSectionDto` (`SectionCodec.cs:202-218`) is the shape written by schemas 9 to 13. `SchemaV8ToV9` writes the current type directly (`Migrations.cs:566`).
- Frozen types reuse current DTOs for sub-records that have not changed. For example `V12.Player` uses `InventoryDto`, `ProgressionDto`, `CompanionDto` and others (`SchemaV12.cs:4-7`, `22-32`).
- Each frozen file's header comment names this obligation: "the step that next changes one of those must freeze a copy of it first" (`SchemaV12.cs:5-7`, `SchemaV8.cs:6-7`).

Current DTO shapes (FACT, `SectionCodec.cs`):
- `PlayerDto` (lines 20-63):
  - `instance_id`, `name`, `x_mm`, `y_mm`, `z_mm`, `appearance_seed`, `inventory[]`;
  - `progression?`, `facing_mdeg?`, `discoveries?`, `equipment?`, `currency?`, `effects?`, `relationships?`, `conversations?`, `quests?`, `companions?`, `posture?`.
  - Each nullable field is "Required from schema N".
- `CellDto` (169-178): `cell_key`, `baseline_hash`, `dirty_reasons[]`, `flags[{name,value}]`, `harvested_nodes[{node_key,last_harvest_tick,harvest_seq}]`, `population_alive[{population_id,alive}]`.
- `EntitiesSectionDto` (202-218): `records[]` (EntityDto), `created[]` (CreatedDto), `baselines[{cell_key,baseline_hash}]`, `containers?[]` (required from 6), `creatures?[]` (required from 8).
- `EntityDto` (288-298): `instance_id`, `slot_key`, `generation_seq`, `def_id`, `dirty_mask`, `state{alive?,x_cm?,z_cm?}`.
- `CreatedDto` (265-279): `instance_id`, `def_id`, `host_cell`, `x_cm`, `z_cm`, `count?`, `quality?`.
- `ContainerDto` (245-252): `key`, `instance_id`, `host_cell`, `items[{item_id,def_id,count,quality?}]`.
- `CreatureDto` (220-243): `key`, `def_id`, `instance_id`, `host_cell`, `generation`, `condition`, `x_mm`, `z_mm`, `facing_mdeg`, `health`, `died_tick`, `respawn_tick`, `mind`, `awareness`, `knows`, `known_x_mm`, `known_z_mm`, `last_seen_tick`, `search_until`, `has_called`.
- Player sub-DTOs: `RelationshipDto{npc_id,dimension,value}` (113-119), `QuestDto` and `ObjectiveDto` (92-111), `CompanionDto` (74-90, `trail_mm` a flat x,z array), `PostureDto` (66-72).

M-status evidence for each bump:
- `docs/M3_STATUS.md:14` (schema 5)
- `docs/M3B_STATUS.md:12` (schema 6)
- `docs/M3C_STATUS.md:12` (schema 7)
- `docs/M3D_STATUS.md:17`, `33` (schema 8)
- `docs/M3E_STATUS.md:18`: "No schema change"
- `docs/M3F_STATUS.md:20` (schema 9)
- `docs/M4_STATUS.md:21` (schema 10): "A trader's wares need nothing new: once touched they are a changed container"
- `docs/M5_STATUS.md:20` (schema 11)
- `docs/M6_STATUS.md:62` (schema 12) and `163` (schema 13): "a schema-13 player without a posture is corrupt, not defaulted ... every older fixture's `expected.json` gained the standing posture and nothing else (reviewed line by line). The player digest is `unnamed.player/v9`."

**Doc lag (FACT):** `docs/PERSISTENCE.md` §6.2's "Implemented chain" table stops at the 10 -> 11 row (`docs/PERSISTENCE.md:344-357`). The 11 -> 12 and 12 -> 13 rows are missing, although the §5.1 prose covers both (`PERSISTENCE.md:196-198`). §5.1 still says "Implemented so far (schema 7)" (`PERSISTENCE.md:200`).

---

## 3. Recipe: how to add schema 14 (worked example: 12 -> 13, commit `232049e`)

### 3.1 What the 12 -> 13 commit touched (FACT, `git show --stat 232049e`)
Persistence-relevant files:
- `src/Persistence/SaveModel.cs`: `SchemaVersion` 12 -> 13 (one line).
- `src/Persistence/Sections/SchemaV12.cs` (new, +33): froze the schema-12 player as `V12.Player`.
- `src/Persistence/Sections/SchemaV11.cs`: header comment only ("Schema 12 left the other sections alone, and so did schema 13").
- `src/Persistence/Migrations.cs` (+46):
  - added `using V12 = ...`;
  - **repointed `SchemaV11ToV12` to write `V12.Player` instead of `PlayerDto`**;
  - added `SchemaV12ToV13`;
  - appended it to `Production`.
- `src/Persistence/SectionCodec.cs` (+24):
  - added `PostureDto` and `PlayerDto.Posture` (nullable, "Required from schema 13");
  - `EncodePlayer` writes it;
  - `DecodePlayer` throws `FormatException` when it is null (`SectionCodec.cs:409`) and validates its values (`SectionCodec.cs:410-416`).
- `src/World/PlayerState.cs` (+56):
  - `Posture` init-property with validation (`PlayerState.cs:306-317`);
  - every `With*` copy method carries `{ Posture = Posture }` (`PlayerState.cs:222-253`);
  - `WithPosture`;
  - the **digest tag bumped from `unnamed.player/v8` to `unnamed.player/v9`**, with posture hashed (`PlayerState.cs:328`, `365`).
- `tests/M2.Probe/M2Fixtures.cs`: the historical player gained `Posture = new Posture(Stance.Crouched, false, 0)` (`tests/M2.Probe/M2Fixtures.cs:157-160`).
- `tests/Persistence.Tests/CanonicalState.cs` (+6): renders `posture`.
- `tests/Persistence.Tests/Fixtures/v13/quick/*` (new, written by the probe) and `v13/expected.json` (new).
- `v1..v12/expected.json`: +5 lines each (the standing posture).
- `tests/Persistence.Tests/Fixtures/README.md`: world-table row and provenance row.
- `tests/Persistence.Tests/HistoricalFixtureTests.cs` (+4): posture assertion (`HistoricalFixtureTests.cs:234-235`).
- `tests/Persistence.Tests/MigrationTests.cs` (+53): the new step test, the "corrupt, not defaulted" test, and every hard-coded step list updated.
- `docs/PERSISTENCE.md` (+2): the §5.1 "Posture (schema 13)" paragraph (the §6.2 table was **not** updated).
- `docs/M6_STATUS.md`.

`SaveLoader.cs` was not touched, because posture holds no definition IDs. The 11 -> 12 commit (`f3a064e`) did touch `SaveLoader.cs` (+10) to put companions' `npc_id` through the definition-ID pass. That is now `SaveLoader.cs:348-354`.

### 3.2 When the new records name definitions (worked example 10 -> 11, commit `0eb6247`, FACT)
That commit also added:
- the writer content pack `tests/Persistence.Tests/Fixtures/content-0.1.6/**`, a full copy of 0.1.5 plus the two quests and the warden's replies;
- a rename in the **current** fixture pack: `Fixtures/content/_aliases.yaml` `quest.fixture.errand: quest.fixture.wardens_errand`, plus `content/quests/fixture/{cull,wardens_errand}.yaml` and `dialogue/fixture/warden_sera.yaml`;
- `M2Fixtures.Historical` `WriterContentVersion`/`WriterContentHash` bumped (`M2Fixtures.cs:109-110`), and `CurrentContentVersion`/`CurrentContentHash` plus the `CurrentContent()` mirror of IDs and aliases (`M2Fixtures.cs:252-283`);
- the README's content-pack narrative (`Fixtures/README.md:25-38`).

Tests pin these:
- `TheProbesContentMirror_IsTheFixtureContentPack` (`tests/Persistence.Tests/MigrationTests.cs:735-...`);
- `TheWritersContentIdentity_IsItsFixturePack`, which **hard-codes `"content-0.1.6"`** (`MigrationTests.cs:754`).

The README states the purpose of each rename: "the rename must reach the X record" (`Fixtures/README.md:51-77`).

### 3.3 Checklist for schema 14 (FACT where cited; the ordering and the "if" branches are INFERENCE)

**A. Domain and world records** (outside Persistence, but the save reads only these):
1. *Player-held state* (for example the player's reputation per faction, crime or bounty records, known-identity records):
   - add to `PlayerRecord` (`src/World/PlayerState.cs:92-369`) as a validated, canonically sorted collection;
   - carry it through **every** `With*` method (`PlayerState.cs:222-253`), as schema 13 did for Posture;
   - include it in `Digest` and **bump the tag** to `unnamed.player/v10` (`PlayerState.cs:323-368`);
   - add it to `Simulation.CaptureRecord` (`src/World/Runtime/Simulation.cs:342-351`) and to the `RuntimeState` constructor (`src/World/Runtime/RuntimeState.cs:97-115`);
   - add a `StateSlice` and an owning system (`RuntimeState.cs:19-74`; `_state.RequireEverySliceOwned()` at `Simulation.cs:139`).
2. *Cell-anchored world state* (for example structures and their pieces):
   - add a record type and a `DeltaSnapshot` init property, sorted (`src/World/WorldDelta.cs:188-200`);
   - add a private store in `WorldDelta`, `internal` mutators and public readers;
   - `TakeSnapshot`: stamp the host cell's `BaselineHash` and define the rebase/retirement rule (`WorldDelta.cs:453-521`; invariant I-7, `docs/PERSISTENCE.md:37`);
   - `FromSnapshot` and a `TryApplyX`: baseline-hash check, validation, the `seenIds` duplicate check, registry registration (`WorldDelta.cs:531-576`, pattern at `795-818`);
   - `EffectiveCellDigest`: include the new records and bump `unnamed.effective-cell/v1` (`WorldDelta.cs:582-635`, tag at 587);
   - add every new public reader to the allow-list in `tests/Architecture.Tests/ArchitectureTests.cs` `WorldDelta_ExposesNoPublicMutation` (`tests/Architecture.Tests/ArchitectureTests.cs:62-78`). The test fails on any unlisted public member.
3. *World-global state* (for example faction-to-faction relations or war state): there is no existing home. **Open design decision** (§11).

**B. Persistence codec** (`src/Persistence/SectionCodec.cs`):
- Add DTOs with `[MessagePackObject]` and snake_case `[Key("...")]`.
- A new field on an existing DTO is declared **nullable**, with the doc comment "Required from schema 14. The 13 -> 14 step gives older saves X." Decode throws `FormatException("... (required from schema 14)")` when it is null (pattern: `SectionCodec.cs:399-409`, `570-581`).
- Validate ranges and enum keys on decode (the posture pattern, `SectionCodec.cs:410-416`).
- Update `EncodePlayer`/`DecodePlayer` or `EncodeEntities`/`DecodeEntitySection` (whose tuple return type grows), or add `EncodeX`/`DecodeX` for a new section.
- If the records are host-cell anchored inside `entities.msgpack`, add them to the `Prove` loop so the `baselines` table covers their host cells (`SectionCodec.cs:481-494`). Decoding looks up the hash via `baselines.GetValueOrDefault(host)` (`SectionCodec.cs:559`, `568`, `579`, `583`).

**C. Freeze the previous shapes** (`Fixtures/README.md:16-17`: "freezes the previous current section shapes as `Sections/V{N-1}` and repoints the step that produces schema N-1 at the frozen types, so every step stays a fixed `V(n) -> V(n+1)` function"):
- **If `PlayerDto` changes:** create `Sections/SchemaV13.cs` with `V13.Player`, an exact copy of the current `PlayerDto` field list (`SectionCodec.cs:20-63`). Repoint `SchemaV12ToV13` to `new V13.Player` (`Migrations.cs:722`).
- **If `EntitiesSectionDto` changes:** freeze the schema-9..13 entities shape, meaning `EntitiesSectionDto` plus any sub-DTO that changes (`CreatedDto`, `ContainerDto`, `ContainerItemDto`, `CreatureDto`, `EntityDto`, `CellBaselineDto`). Repoint **`SchemaV8ToV9`**, which writes `new EntitiesSectionDto` (`Migrations.cs:566-582`).
  - Precedent for naming is mixed: the entities shape written by schemas 6-7 was frozen as `V6.EntitiesSection` (`SchemaV6.cs:4-7`); the player written by 7-8 as `V8.Player` (`SchemaV8.cs:4-5`). The README rule says `V{N-1}`, which gives `V13`.
- **If any sub-DTO shared by frozen types changes**, copy it into every frozen namespace that references it first. Examples: `ProgressionDto` is referenced by `V4..V12.Player`; `CreatureDto` by `V8.EntitiesSection`; `RelationshipDto` by `V10..V12`.
- **If `CellDto` changes:** only `SchemaV1ToV2` writes cells, and it already writes the frozen `V2` types (`Migrations.cs:164-215`). The new step reads `V2.CellsSection` and writes the new shape.

**D. The migration step** (`src/Persistence/Migrations.cs`):
- Add `public sealed class SchemaV13ToV14 : SchemaMigration { From => 13; Summary => "schema 13 -> 14: ..."; Apply(document, environment, report) }`. The `Summary` must start with `"schema 13 -> 14:"`, because tests slice `s[..14]`.
- It deserializes the frozen V13 shape from `document.Sections[SaveFormat.X]` with `SectionCodec.MessagePackOptions` and writes the current DTO with empty defaults ("none before M7").
- It must: touch no filesystem; not resolve definition IDs ("treats them as opaque strings"; `Migrations.cs:49-53`, `docs/PERSISTENCE.md:342`); report a blocker rather than guess; pass a null (quarantined) section through as null (`Migrations.cs:42`, checked via `GetValueOrDefault(...) is { }`).
- It sets `document.Manifest["schema_version"] = To` and calls `report.Steps.Add(Summary)`.
- Append it to `SchemaMigrations.Production` (`Migrations.cs:69-81`). "A schema bump adds exactly one step here, plus a fixture" (`Migrations.cs:68`).
- A **new section file** needs the step to *add* a key to `document.Sections` (a `Dictionary<string, byte[]?>`, `Migrations.cs:43`). `SaveLoader` would also have to read it (currently hard-coded to three files at `SaveLoader.cs:96`), and integrity must stop requiring it for schemas below 14 (see §0 item 2).

**E. The load pipeline** (`src/Persistence/SaveLoader.cs`):
- `ResolveDefinitions` (`SaveLoader.cs:206-359`) must resolve **every stored definition ID** in the new records: faction IDs, building/piece definition IDs, NPC IDs in assignments, and any `world.*` flags. It must define the merge rule for "two records that resolve to one ID", following the quest and companion patterns (`SaveLoader.cs:334-354`). A new `DeltaSnapshot` property must be copied into the reconstructed snapshot (`SaveLoader.cs:356-358`).
- `ProveBaselines` must `Check(host_cell, baseline_hash)` for each new cell-anchored record (`SaveLoader.cs:380-389`).
- `SemanticRebase.Apply` must carry the new records onto a new baseline and add them to the returned `DeltaSnapshot` (`src/Persistence/BaselineTransitions.cs:97-117`). The existing rule for authored or created things is "moves to the new baseline as it is" (`BaselineTransitions.cs:94-108`).
- Any new decode goes through `DecodeOrQuarantine` so a corrupt world section degrades to a quarantine rather than failing the load (`SaveLoader.cs:142-146`, `410-430`). Player decode failure stays fatal (`SaveLoader.cs:147-155`).

**F. Fixtures** (`tests/Persistence.Tests/Fixtures/README.md:9-23`):
1. Extend `M2Fixtures.Historical.Player()` and/or `World(Registry)` (`tests/M2.Probe/M2Fixtures.cs:112-248`) with M7 state. Use deterministic IDs via `EntityId.Create(kind, ts, bytes)`, as the file does (`M2Fixtures.cs:61-62`, `162`, `250`).
2. If the new records name new definitions:
   - add a writer pack `Fixtures/content-0.1.7/` (0.1.6 plus the new faction and building definitions) and bump `WriterContentVersion`/`WriterContentHash`;
   - change the hard-coded `"content-0.1.6"` in `MigrationTests.cs:754`;
   - add the definitions to the current pack `Fixtures/content/` as 0.2.9, ideally with **one rename via `_aliases.yaml`** so the fixture proves "the rename must reach the <faction/structure> record";
   - bump `Fixtures.ContentVersion = "0.2.8"` (`HistoricalFixtureTests.cs:16`) and `M2Fixtures.Historical.CurrentContentVersion/Hash` and the `CurrentContent()` ID and alias mirror (`M2Fixtures.cs:252-283`).
3. Build, then run `dotnet tests/M2.Probe/bin/Debug/net8.0/M2.Probe.dll fixture <dir>` and copy `<dir>/quick` to `Fixtures/v14/quick` (`Fixtures/README.md:14`; the probe command is at `tests/M2.Probe/Program.cs:16-19`). **Never edit v1..v13 bytes.** `.gitattributes` stores `tests/Persistence.Tests/Fixtures/v*/quick/**` as binary.
4. Add the new fields to `CanonicalState.Render` (`tests/Persistence.Tests/CanonicalState.cs:17-269`, hand-written field by field).
5. Run the fixture tests once with `UNNAMED_WRITE_FIXTURE_EXPECTATIONS=1` (`HistoricalFixtureTests.cs:107-108`). This regenerates **all 14** `expected.json` files. Review each diff line by line: older fixtures should gain only the empty M7 fields. "Never change it to make a failing migration pass" (`Fixtures/README.md:20-22`).
6. Update the README world table (`Fixtures/README.md:47-77`) and the Provenance table (`Fixtures/README.md:81-95`).

**G. Tests that must change or be added** (FACT: where the schema number is hard-coded):
- `MigrationTests.cs:78`: `Assert.Equal(11, report.Steps.Count)` becomes 12. Also lines 79-89 (add `"schema 13 -> 14:"`).
- Step-prefix lists at `MigrationTests.cs:104`, `124`, `142`, `162`, `178`, `194`, `214`, `230`, `246` (each must gain `"schema 13 -> 1"`).
- `MigrationTests.cs:262`: `Assert.Single(report.Steps)` for 12 -> 13 must become a two-step list.
- `MigrationTests.cs:398`: 12 steps becomes 13, plus a new `StartsWith("schema 13 -> 14:")`.
- `MigrationTests.cs:428`: the message "no registered migration chain from schema 1 to schema 13" changes to 14.
- New tests, following the existing pattern:
  - `Schema13To14_Gives...` (the pattern at `MigrationTests.cs:256-268`);
  - `ASchema14PlayerWithout<X>_IsCorrupt_NotDefaulted` (the pattern at `MigrationTests.cs:270-278`);
  - definition-pass coverage for new IDs (rename, removal with replacement, discard, unresolved), following `TheProgressionRecord_GoesThroughTheDefinitionPass_RenamesAndRemovals`;
  - baseline proof and rebase for any new cell-anchored record, following `ACreatedInstance_IsProvenAgainstItsHostCell_AndRebasedOnlyByATransition`.
- `HistoricalFixtureTests.Fixture_LoadsToItsExpectedCurrentState`:
  - add an M7 block (`schema >= 14 ? ... : empty`);
  - extend the per-schema alias expectations (`HistoricalFixtureTests.cs:239-287`);
  - revisit `CellsMatched` (`schema switch { >= 8 => 10, >= 6 => 9, >= 3 => 7, _ => 6 }`, line 288), because a record in a new cell changes the count. The fixture world uses `TenCells` (`M2Fixtures.cs:37-38`) and all ten are already used.
- Generic tests that need no edit: `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture` (`HistoricalFixtureTests.cs:81-96`), `TheProductionChain_HasOneStepPerVersion_InOrder` (`MigrationTests.cs:415-...`), `SaveToolTests` (uses `SaveFormat.SchemaVersion`), `RoundTripTests.cs:46`.
- Round-trip tests (T-01): extend `RoundTripTests` or add Application-level save/load tests, as M4 and M5 did (`WhatWasSaidAndThought_ContinuesAcrossASaveAndLoad`, `docs/M4_STATUS.md:48`; `QuestState_ContinuesAcrossASaveAndLoad`, `docs/M5_STATUS.md:71`).
- The kill-mid-migration test (`MigrationTests.cs:713`, `AMigrationKilledAtAnyStep_...`) runs against v1 and the current version (`MigrationTests.cs:726`). It needs no edit, but will then exercise v14.

**H. Documentation:**
- `docs/PERSISTENCE.md`: a §5.1 or §5.3 paragraph for schema 14 (the posture paragraph is the model, `PERSISTENCE.md:198`); §6.2 chain-table rows 11 -> 12, 12 -> 13 and 13 -> 14 (the table stops at 10 -> 11); §3.2 layout if a file is added; §6.4 if a transition is registered.
- `DATA_MODEL.md` §6's "Save-version-sensitive surfaces" (`docs/DATA_MODEL.md:846`).
- The M7 status doc.
- `Fixtures/README.md`.

---

## 4. World deltas as serialized today

### 4.1 Model (FACT)
- "Load = generate(baseline) + apply(delta); save = diff against the regenerated baseline ... Nothing that equals the baseline is stored" (`src/World/WorldDelta.cs:208-214`; `docs/PERSISTENCE.md:41-49`).
- The baseline cache `_baselines` is "a transient cache: never persisted, always reproducible" (`WorldDelta.cs:239-245`).
- `TakeSnapshot` is the "save-time diff" and "the authority": it deletes records at baseline and rebases (`WorldDelta.cs:446-452`). The mutation paths are only a hint (`docs/PERSISTENCE.md:214-219`).

### 4.2 `cells.msgpack` (FACT)
`CellDeltaRecord(CellKey, BaselineHash, DirtyReasons, Flags, HarvestedNodes, PopulationAlive)` (`WorldDelta.cs:18-24`) maps to `CellDto` (`SectionCodec.cs:169-178`). A cell record is written only when the cell's `MutableCell` is not at baseline: flags, nodes or populations non-empty (`WorldDelta.cs:828`, `484-488`).
- **Flags:** `world.*` definition IDs mapped to a `long`. Value 0 is the baseline and removes the entry (`WorldDelta.cs:250-259`). The ID must be a valid definition ID with the `world.` prefix (`WorldDelta.cs:252-253`, `686-687`). Flags are declared content (`docs/DATA_MODEL.md:68-70`). **The def-ID pass resolves flag names like any definition** (`SaveLoader.cs:242-251`): an undeclared or renamed flag with no alias is a blocker. Callers always choose a cell:
  - a door's or switch's cell (`src/World/Runtime/Systems.cs:269`, `288`);
  - a quest reward's location cell (`Quests.cs:170`);
  - the speaker's cell for a dialogue consequence (`Social.cs:368`).
- **Harvested nodes:** `NodeHarvest(NodeKey, LastHarvestTick, HarvestSeq)`. Absence means available (`WorldDelta.cs:10-11`). A regrow removes the record (`WorldDelta.cs:283-287`). Node keys are `node.<cell>.<rule>.<NN>` (`docs/PERSISTENCE.md:256`, `266`).
- **Population alive:** `population_id` mapped to a count, stored only when it differs from `Target`. It is validated against the baseline's `[0, Max]` (`WorldDelta.cs:298-309`, `697-706`).
- **`dirty_reasons`** is recomputed at save from `flags`, `nodes`, `spawns` and `entities` (`WorldDelta.cs:489-494`). It is a hint only. The doc also lists `buildings` and `terrain` reasons and an `overrides` field (`docs/PERSISTENCE.md:210-212`); **neither exists in code** (M2 deferred cell `overrides`, `docs/M2_STATUS.md:84`).

### 4.3 `entities.msgpack` (FACT)
One section holds five record kinds plus a `baselines` table (`SectionCodec.cs:202-218`).

1. **Slot records** (`EntityDeltaRecord`, `WorldDelta.cs:32-46`): the divergence of a *baseline-generated* population member, keyed by `slot_key = <cell_key>.<population_id>.<ordinal>` (`WorldDelta.cs:665-672`).
   - Removal of a baseline object is encoded as `Alive=false` (a tombstone; `DirtyAlive = 1`).
   - A move sets `XCm`/`ZCm` (`DirtyPosition = 2`), in cell-relative cm, because slot coordinates are drawn in `[0, CellSizeCm)` (`src/World/Generation.cs:267-269`).
   - A null field is the baseline value. Promotion to a record gives the member a registry ULID (`WorldDelta.cs:646-659`).
   - `RestoreOccupant` nulls the fields, and the next `TakeSnapshot` deletes the record and **destroys the identity** (`WorldDelta.cs:330-334`, `466-473`).
   - Load is a **merge on slot key, not an append**. "Two records claim one slot" is rejected (`WorldDelta.cs:729-730`; `docs/PERSISTENCE.md:234`).
2. **Created instances** (`CreatedEntityRecord`, `WorldDelta.cs:54-61`): **addition of a new object** that no baseline generates (a dropped stack, a placed chest).
   - Stored whole: `instance_id`, `def_id`, `host_cell`, `x_cm`, `z_cm` (**cell-relative, in `[0, 10000)`**, `WorldDelta.cs:349-350`, `443`, `752-753`), `count` (schema 6) and `quality` (schema 9).
   - Removal is deletion of the record (`TakeCreated` or `RemoveCreated`; `WorldDelta.cs:370-371`, `431-435`).
   - The ground drop computes the host cell from the body position and converts to cell-relative cm (`src/World/Runtime/Items.cs:584-593`).
3. **Changed containers** (`ContainerRecord`, `WorldDelta.cs:76`): an *authored* container (or a corpse) whose contents changed, stored with its whole contents: `key`, `cnt_` instance ID, `host_cell`, and items with `itm_` IDs.
   - "A changed container stays recorded even if its contents come back to what they were: its baseline is content, which this layer never sees" (`WorldDelta.cs:510-511`). This is **an explicit exception to rebase**.
   - `RemoveContainer` (for example a corpse looted empty) retires the container's and items' identities (`WorldDelta.cs:390-400`).
   - A trader's wares use this record, keyed by the merchant profile (`docs/PERSISTENCE.md:192`).
4. **Creature records** (`CreatureRecord`, `WorldDelta.cs:162-185`): a spawner member that has left its baseline, keyed `spawner#member` (validated `WorldDelta.cs:802-804`). It carries:
   - `Generation` (each generation has its own identity);
   - `Condition` (`alive`/`corpse`/`gone`; "gone" is the removed state);
   - position in **absolute mm**, facing in millidegrees `[0, 360000)`, health, `DiedTick`, `RespawnTick`;
   - mind fields (`Mind`, `Awareness 0..100`, `Knows`, known position, `LastSeenTick`, `SearchUntil`, `HasCalled`).

   `RemoveCreature` returns it to baseline and retires its ID (`WorldDelta.cs:424-428`).
5. **`baselines`** (`CellBaselineDto`): `cell_key` mapped to `baseline_hash` for every host cell referenced by records 1-4. `EncodeEntities` throws if two records disagree about one host cell's hash (`SectionCodec.cs:481-494`).

### 4.4 `baseline_hash` usage (FACT)
- `CellBaseline.Digest` = `CanonicalHasher("unnamed.cell-baseline/v1", cell, TerrainHash, nodes[key, def, x, z], populations[id, family, target, min, max, slots[key, ordinal, x, z]])` (`src/World/Generation.cs:144-162`). It covers generated **outputs** only.
- Every snapshot record is stamped with its host cell's current hash at save (`WorldDelta.cs:474`, `498`, `505-518`).
- At load:
  - `ProveBaselines` regenerates each referenced cell and compares (`SaveLoader.cs:361-408`);
  - on a mismatch, it looks for a registered `BaselineTransition` keyed by the exact `(save fingerprint, running fingerprint)` pair (`SaveLoader.cs:395-396`), else refuses and lists every cell (`SaveLoader.cs:397-405`);
  - the world builder independently rejects any record whose hash differs: "the last line" (`WorldDelta.cs:523-530`).
- `SemanticRebase` carries a node or population record only if the same key or budget exists in the new baseline, and a slot record only if the slot exists with the same family (`BaselineTransitions.cs` roughly lines 40-92). Created instances, containers and creatures move "as they are" (`BaselineTransitions.cs:94-108`). A vanished target is reported loss if `DropVanishedTargets`, otherwise a blocker (`BaselineTransitions.cs:40-46`).

### 4.5 Implications for structures (INFERENCE)
- The existing kind closest to a player structure is the **created instance**: ULID-keyed, whole-record, host-cell anchored, proven by hash, carried as-is by transitions.
  - Its coordinates are **cell-relative cm confined to one cell**. A structure straddling a seam (ROADMAP M7 exit criterion, `docs/ROADMAP.md:284`) cannot use that convention for its footprint.
  - `WORLD_ARCHITECTURE.md` §10 says: "Ownership/geometry is stored once on the structure and referenced by each cell's delta, so a cell boundary cannot split a structure's state" (`docs/WORLD_ARCHITECTURE.md:403-417`).
  - Creatures and companions already use absolute mm with a `host_cell` for proof, so a structure record could carry absolute mm plus one designated host cell (and perhaps the set of covered cells for proof).
- The 2..13 codec allows only **one baseline hash per host cell per section** (`SectionCodec.cs:483-485`). A record spanning cells would need to prove all of them. `ProveBaselines.Check` is per `(cellKey, hash)` pair and could be called for each covered cell.
- **Removing an authored region object** (an authored wall, a door) is **not expressible** today except through flag conventions: doors and barriers use `world.*` flags (`src/Domain/Spatial/RegionLayout.cs:93-97`). Authored layout structures are content, not generated baseline, so a delta cannot tombstone one.

---

## 5. Instance IDs and registry persistence

- **Format (FACT).** `<prefix>_<ULID>`: a 3-letter prefix, `_`, then 26 Crockford base32 characters (48-bit ms timestamp plus 80 random bits) (`src/Domain/EntityId.cs:13-35`).
- **Generation.** `EntityId.NewId` uses `DateTimeOffset.UtcNow` and `RandomNumberGenerator` (`EntityId.cs:48-53`). `EntityId.Create(kind, ts, random)` is "For fixtures and tests; runtime code uses NewId" (`EntityId.cs:55-58`).
- **Prefixes** (`src/Domain/EntityKind.cs:35-50`): `itm`, `npc`, `crt`, **`bld` (Building)**, `cnt`, `qst`, `crp`, `anc`, `sum`, `plt`, `evt`, `chr`. There is **no piece prefix**. `docs/PERSISTENCE.md:250` says "pieces are ULID-keyed rows" but `DATA_MODEL.md` §2.2 lists no piece prefix (`docs/DATA_MODEL.md:121-123`). Open question.
- **Inferring kind from a definition.** Inference works only for `item`, `creature`, `npc` and `quest` definition prefixes (`EntityKind.cs:71-84`). Any other kind, such as a building or a faction, must pass an explicit `EntityKind` (`src/EntityRegistry/EntityRegistry.cs:74-85`).
- **The registry is never saved (FACT).** It is rebuilt at load by registering every persisted identity:
  - `FromSnapshot` → `TryApplyEntity/Created/Container/Creature` → `_registry.CreateEntity(def, id)` (`WorldDelta.cs:738`, `761`, `788-790`, `815`);
  - carried inventory in `Simulation`'s constructor, which fails if an ID is both carried and in the world (`Simulation.cs:107-113`);
  - named NPCs in `NpcSystem.Populate`.

  Duplicate IDs across records become `RejectedRecord`s ("appears twice" / "already registered", `WorldDelta.cs:731-739`, `754-757`, `779-784`, `811-814`). The load reports them as warnings, and the load is then not "complete".
- **Deterministic content-derived IDs (FACT; they contradict the "never build one by hand" rule, see §10):**
  - Named NPCs: `NpcSystem.InstanceIdOf(npcId)` = `EntityId.Create(Npc, 1, hash("unnamed.npc/v1", npcId)[0..10])`: "the same instance in every world and load" (`src/World/Runtime/Social.cs:120-125` in file numbering; lines 36-41 of the excerpt read). NPC bodies are **not saved** (transient in Phase 1), except companions'.
  - Spawner creatures: `Identity(key, generation)` = `EntityId.Create(Creature, 1, hash("unnamed.creature/v2", key, generation))` (`src/World/Runtime/Creatures.cs:160-165`). A creature record stores its `instance_id` anyway (`SectionCodec.cs:225`).
- **Player-record references by ID:** inventory `item_id`; equipment slots naming carried `item_id`s (validated: "which is not in the inventory", `PlayerState.cs:133-141`); companions and relationships by **NPC definition ID** (`npc.*`), not instance ULID (`PlayerState.cs:154-163`, `197-207`).
- **Reference-by-definition-ID is the Phase-1 norm for NPCs.** INFERENCE: faction membership, witnesses and building assignments naming named NPCs can use `npc.*` definition IDs, which also go through the def-ID pass. Assignments naming generic or promoted instances would need ULIDs plus the §7.2 "dangling reference" handling, which is unimplemented ("Domain invariant validation on load ... Until then rebase retires an entity's identity without a reference check", `docs/M2_STATUS.md:86`).
- `DATA_MODEL.md` §6 lists "the registry's ULID anchor clock" as a save-version-sensitive surface (`docs/DATA_MODEL.md:846`). **No such clock exists or is saved** (FACT from the code above).

---

## 6. Content identity, `content_hash`, the fingerprint, and new content

### 6.1 `content_hash` (FACT)
- `ContentLoader.ComputeContentHash()` = `CanonicalHasher("unnamed.content-hash/v1", count, sorted (id, normalized YAML source) ..., "_aliases.yaml", normalized text)` (`src/Content/ContentLoader.cs:762-775`). **Any added, removed or edited definition, or any alias-map edit, changes it.**
- `GameSession.Boot` builds `ContentIdentity(options.ContentVersion ("0.3.0"), hash, loader.Definitions.Keys, loader.Aliases, loader.Removed, loader.Discarded)` (`src/Application/GameSession.cs:21`, `100-101`).
- **At load:** if `manifest.ContentHash != context.Content.Hash`, `ResolveDefinitions` runs over the player and delta (`SaveLoader.cs:157-161`). Each stored ID resolves (`ContentIdentity.Resolve`, `src/Persistence/ContentIdentity.cs:67-100`) to one of:
  - `Current`;
  - `Renamed` (via `aliases`);
  - `Replaced` (`removed: old: new`);
  - `Discarded` (`removed: old: ~`, reported loss and the record dropped);
  - `Unresolved` (a **blocker** naming the ID and `content/_aliases.yaml`).

  Renames may chain; a cycle is unresolved.
- **Adding new definitions or new content kinds:** existing stored IDs still resolve as `Current`, so there are **no blockers and no loss**.
  - The report does record `ContentChanged = true` (`src/Persistence/MigrationReport.cs:77`). That makes `ChangesSave` true (`MigrationReport.cs:82-84`) and `Result = Ready` ("READY TO MIGRATE"), not `UpToDate` (`MigrationReport.cs:86-87`).
  - Consequences: the load succeeds, but `SaveStore.LoadFrom` does **not** record load-proof in `rotation.json` (it requires `UpToDate`, `SaveStore.cs:217-218`). `save:migrate --dry-run` reports "READY TO MIGRATE". The next save writes the new hash.
  - INFERENCE: M7 content additions (factions, building pieces, new world flags) need no alias entries and no schema bump **by themselves**. The schema bump comes from new *persisted state shapes*.
- **Removing or renaming** a definition referenced by a save requires an `_aliases.yaml` entry. The historical fixtures enforce this in CI (`RemovingTheAliasFromTheContentPack_FailsTheFixtureLoads`; `docs/DATA_MODEL.md:117-119`).
- **Tuning** (a content value that a *persisted derived value* was computed from) is "baseline-locked" and needs a migration step, "never the `content_hash` fast path" (`docs/PERSISTENCE.md:276`, `324`; RK-P12). INFERENCE, relevant to reputation: store raw reputation values and derive tiers at run time. `DATA_MODEL.md` §6 warns that faction "threshold changes retroactively reclassify a player" (`docs/DATA_MODEL.md:846`). Storing a tier would make thresholds baseline-locked.

### 6.2 Worldgen fingerprint and baseline transitions (FACT)
- `worldgen_fingerprint` = `hash("unnamed.worldgen-fingerprint/v1", GeneratorId, WorldgenVersion, RngContractVersion, Profile.Digest, CanonicalProbes.Digest)` (`src/World/Generation.cs:306-319`).
  - `Profile.Digest` covers node rules, population rules and terrain, **plus authored fixed nodes only when there are any** ("so every earlier profile keeps its digest", `Generation.cs:101-120`).
  - The probes are seed `0x0DDB1A5E5BAD5EED` over four cells (`Generation.cs:281-299`).
- A fingerprint change is only a **warning** (`SaveLoader.cs:171-172`). Cells are decided by `baseline_hash`. A different `worldgen_version` or RNG contract with no registered migration is a blocker (`SaveLoader.cs:164-170`).
- The game registers transitions at boot (`GameSession.cs:107-117`):
  - "M3f: the region's resource nodes": from the fingerprint of a profile without fixed nodes, to current;
  - "M6: the content bible's four cells": from `M3LayoutFingerprint = "sha256:4c97504c..."`, a frozen constant (`GameSession.cs:77`), to current, with `DropVanishedTargets: true`.
- Both transitions target **the running fingerprint**. There is no chaining: a transition must exist from each historical fingerprint to the current one (`SaveLoader.cs:395-396`).
- INFERENCE for M7:
  - If M7 changes placement data (fixed nodes, populations, terrain), the M6 fingerprint must be frozen as a constant and a "M7: ..." transition added. Otherwise every M6 save with a changed cell in the affected area refuses.
  - Structures authored in the region layout (`RegionLayout` structures, doors, switches, barriers) are *not* generator input. Adding them moves no fingerprint and no baseline hash; that is Class A content (`docs/PERSISTENCE.md:330`).
- **`SaveTool` cannot see the game's transitions or authored nodes.** FACT: it builds `LoadContext` with no `Transitions` (`src/SaveTool/SaveToolApp.cs:62`), and `WorldgenProfileFile.Read` has no fixed nodes (`SaveToolApp.cs:150-158`). INFERENCE: `save:migrate --dry-run` on a real Ashen Hollow save would refuse or mis-prove. The tool is effectively fixture-grade. Worth fixing if M7 relies on it.

---

## 7. Derived, not saved (FACT unless marked)

| Thing | Where rebuilt | Evidence |
|---|---|---|
| Cell baselines (terrain signature, nodes, population slots) | `WorldDelta.Baseline` cache, lazily per cell | `src/World/WorldDelta.cs:239-245` |
| Static walk space / collision (region structures, bounds, terrain grid) | Content at boot: `WorldContent.BuildLayout` → `SimulationSetup.Layout.Space` | `src/Application/GameSession.cs:86-89`; `src/Domain/Spatial/RegionLayout.cs:60-67` |
| Dynamic blockers (closed doors, standing barriers, living creatures and NPCs) | Computed on demand, `_context.Obstacles()` | `src/World/Runtime/Simulation.cs:254-255`; `RegionLayout.cs:93-97` |
| Door, switch and barrier open state | Derived from cell `world.*` flags | `Simulation.cs:219-223` |
| Per-cell simulation tier | `TierSystem.Settle()` at construction ("A hard boundary ... settles directly, with no events") | `src/World/Runtime/RuntimeState.cs:36`; `src/World/Runtime/Systems.cs:470-480`; `Simulation.cs:144` |
| Player combat state (swing phase, guard, dodge, stagger) | `PlayerCombat.Rested` ("a load starts at rest") | `RuntimeState.cs:48`, `128`; `src/World/Runtime/Combat.cs:233` |
| Named NPC bodies and identities | `NpcSystem.Populate()` from `Layout.Npcs` ("nothing about their bodies needs saving"); IDs derived | `src/World/Runtime/Social.cs:87` (NpcState transient), `101-104`, `127-139`; `Simulation.cs:141` |
| Open conversation | Transient ("a load starts outside any conversation") | `Social.cs:90` |
| Quest debugger trace | Transient | `src/World/Runtime/Quests.cs:205` |
| Movement intent | Transient ("never saved; the posture is") | `Systems.cs:106-107` |
| Creature baseline placement and generation-0 identity | `CreatureSystem.Populate()`: RNG channel `spawn/<key>`, up to 32 samples tested with `Kinematics.IsClear` against `Layout.Space` and earlier creatures; the record's divergence then overlaid | `src/World/Runtime/Creatures.cs:171-215` |
| Creature wander and patrol | Derived from the world tick ("need no storage"); an attack windup is transient | `docs/PERSISTENCE.md:246`; `docs/M3D_STATUS.md:33` |
| Companions (roster saved) | `CompanionSystem.Populate()` re-places companion NPC bodies from the saved records | `src/World/Runtime/Companions.cs:125-143` |
| Pool maxima, attribute totals, derived stats | `ProgressionEngine.Derive` at run time; a full pool is stored as null | `docs/PERSISTENCE.md:200`; `src/Persistence/Sections/ProgressionSection.cs:71-79` |
| Node readiness | Derived from `last_harvest_tick`/`harvest_seq` and the world day | `docs/PERSISTENCE.md:266` |
| Container baseline contents | Loot table rolled from the semantic key until first change | `docs/PERSISTENCE.md:242` |
| `dirty_reasons` | Recomputed at save | `WorldDelta.cs:489-494` |
| Entity registry | Re-registered from records at load | §5 above |
| `flags.quarantined_sections` | Set on the load result | `src/Persistence/SaveLoader.cs:185-188` |
| Command log | **Not saved at all** (`command_log_sha256` null) | `src/Persistence/SaveStore.cs:474` |

Documented but not built:
- `docs/PERSISTENCE.md:83-86` lists "Navmesh, collision, occlusion ... Baking / streaming pipeline", "Pathfinding, AI blackboards ... Fresh instantiation on tier promotion", and "Derived caches (encumbrance, faction power, settlement wealth) | Recomputed **once**, after every migration and alias resolution (§7.4 step g)".
- The §7.4 list puts that recompute at step **k**, not g: the §2 table's "step g" is stale lettering (`PERSISTENCE.md:492`).
- SYSTEMS S-32 lists "navmesh dirty regions" as transient (`docs/SYSTEMS.md:356`). S-27 lists "Witness-propagation working set, service-availability cache, guard-alert state per settlement" as transient (`docs/SYSTEMS.md:308`).

---

## 8. Load order (normative §7.4 vs code), and where a new derived structure is rebuilt

### 8.1 Normative (FACT, `docs/PERSISTENCE.md:478-513`)
Steps:
- a. manifest
- b. integrity
- c. schema chain
- d. definition IDs
- e. generator contract
- f. baseline proof
- g. create the store and registry
- h. regenerate the baseline
- i. apply the cell delta
- j. apply the entity delta (merge on `slot_key`)
- k. **recompute derived caches, "ONLY here, after c–j. Never incrementally"** (I-6, `PERSISTENCE.md:36`)
- l. deserialize player and companion state
- m. invariant validation
- n. **one** `WorldLoaded` event

As-implemented note: "Steps k and n arrive with the first derived caches and the Application wiring. Step l's player decode runs before d, because the definition-ID pass rewrites inventory references" (`PERSISTENCE.md:499`). The authority order is M2b §16: format, integrity, schema chain, definition IDs, baseline, invariants, commit; "Do not recompute derived caches until migration and delta application are complete" (`docs/M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md:793-805`).

### 8.2 Code (FACT)
1. `SaveStore.Load` (`SaveStore.cs:194-199`) calls `LoadFrom`, which calls `SaveLoader.Run` (`SaveLoader.cs:39-189`):
   - a (lines 45-66);
   - b (68-97);
   - c (99-129);
   - decode, which includes the player: step l moved early (131-155);
   - d (157-161);
   - e (163-172);
   - f (174-178);
   - g-j in one call, `WorldDelta.FromSnapshot(generator, seed, context.Registry, delta, out rejected)` (180-183);
   - the result is `LoadResult` (185-188).
2. `GameSession.Load` (`GameSession.cs:147-152`) calls `Simulation.Start(Setup, result.Player, result.World, result.Manifest.WorldTick, _bus)`, which runs the `Simulation` constructor (`Simulation.cs:100-145`):
   - `RuntimeState`;
   - carried inventory registration;
   - systems composed in fixed order, slices claimed;
   - `RequireEverySliceOwned()`;
   - `_effects.Seed`;
   - `_npcs.Populate()`;
   - `_companions.Populate()`;
   - `_creatures.Populate()`;
   - `_tiers.Settle()`.
3. **There is no `WorldLoaded` event** (grep: none in `src/`). M2 deferred it (`docs/M2_STATUS.md:85`). Domain invariant validation on load (step m, cross-reference checks) is also deferred (`docs/M2_STATUS.md:86`). Only per-record validation in `TryApply*` exists.
4. New game: `GameSession.NewGame` → `new WorldDelta(...)` (empty) → the same `Simulation.Start` (`GameSession.cs:135-144`). **The constructor is the single rebuild point for both paths.**

### 8.3 Where nav data should be rebuilt (INFERENCE)
- The de facto §7.4 step k is the `Simulation` constructor, after `FromSnapshot` and before any system's `Populate`.
- Rebuild the derived navigation and collision structure there, from content `Layout.Space` plus the loaded structure records. Build it **before** `_creatures.Populate()`, because baseline creature placement tests clearance against the walk space (`Creatures.cs:191`). If structures should block spawns, they must be in that space first. Note the determinism consequence: a baseline creature's re-placed position would then depend on player structures. It is saved only once the creature diverges.
- Rebuild it on every structure mutation through the owning system (incremental at run time is fine; the I-6 rule is about load). At load it must be computed once from the final migrated, alias-resolved, proven state.
- It must never be persisted. If it were cached, `docs/PERSISTENCE.md:86` requires `derived: true` and discarding it "on any doubt".
- Presentation reads the rebuilt data for prediction the same way it reads `DynamicBlockers` (`Simulation.cs:254-255`). This fits owner ruling 1.

---

## 9. StateDump, the "415 fields" comparison, and the other equality checks

### 9.1 `StateDump` (FACT, `src/Application/StateDump.cs`)
- `Render(Simulation, replayable=false)` builds a JSON object with:
  - `world_tick`;
  - `player`: `JsonSerializer.SerializeToNode(simulation.CaptureRecord())`, reflection over **every public property of `PlayerRecord`, including `Digest`** and the `Progression` object;
  - `world`: `SerializeToNode(simulation.World.TakeSnapshot())`, every public property of `DeltaSnapshot`.

  Enums are written as strings (`StateDump.cs:25-46`). Note that `TakeSnapshot()` rebases (mutates) the world, like a save.
- The replayable variant removes `AppearanceSeed` and `Digest` from the player, sorts object arrays by their ID-masked content, and renames instance IDs to `kind#n` (`StateDump.cs:35-44`, `96-191`).
- `Compare(expected, actual, out leaves)` walks both trees. Each scalar leaf counts 1; each missing key counts 1 and is a difference; an array-length mismatch counts 1 (`StateDump.cs:49-90`).
- **The 415 figure** is the leaf count of the M6 acceptance run's state after `--playthrough-verify`: "415 fields compared, 0 differences (412 before, plus the posture)" (`docs/M6_STATUS.md:172`). Code: `src/Presentation/Playthrough.cs:422-459` (`SaveAndDump` writes `state_saved.json` and `state_replay.json`; `LoadAndCompare` renders after the load, compares, writes `state_diff.txt`, and records a failure when differences > 0).
- The Application-level test `ASaveAndALoad_CompareEqual_FieldByField` requires 0 differences and `leaves > 100` (`tests/Application.Tests/StateDumpTests.cs:19-34`). The 415 is **not a constant anywhere**: it is simply the size of that run's state.
- **How new fields enter it:**
  - automatically, if they are public properties of `PlayerRecord` or `DeltaSnapshot` (or nested records). They must be System.Text.Json-serializable: immutable collections and records are fine, and `EntityId` has its own converter (`src/Domain/EntityId.cs:22`);
  - **not at all**, if M7 state lives elsewhere (a separate faction store or a world-global record outside `WorldDelta`), unless `StateDump.Render` adds a root key (for example `root["factions"]`).
- INFERENCE: expect the playthrough count to rise with M7 state. The pass criterion is 0 differences.

### 9.2 Other equality checks that are hand-written (FACT)
- **`CanonicalState.Render`** (`tests/Persistence.Tests/CanonicalState.cs:17-269`) is the `expected.json` format for historical fixtures. It is explicit, field by field. A new schema field is not covered until it is written here.
- **`PlayerRecord.Digest`** (`src/World/PlayerState.cs:323-368`) is used by T-01 round trips (`tests/Persistence.Tests/RoundTripTests.cs:39`, `70-71`) and by `Simulation.StateDigest`.
- **`WorldDelta.EffectiveCellDigest`** (`src/World/WorldDelta.cs:582-635`) covers flags, nodes, created, containers, creatures, population counts and slot occupants per cell.
- **`Simulation.StateDigest`** = `hash("unnamed.simulation/v1", tick, PlayerRecord.Digest, per-region-cell EffectiveCellDigest)` (`src/World/Runtime/Simulation.cs:357-364`). It is used by the headless smoke ("the quicksave's digest identical", `docs/M6_STATUS.md:173`) and by replay comparisons.
- INFERENCE: new structure or faction state must be added to the digests. Otherwise the smoke and digest-based tests pass while that state is lost. Bump the digest tags when coverage changes, as schema 13 bumped `unnamed.player/v8` to `v9`.

---

## 10. Contradictions and tensions found (with citations)

1. **Layout vs code.**
   - `docs/PERSISTENCE.md:106-121` lists `companions.msgpack`, `buildings.msgpack`, `journal.jsonl`, `command_log.jsonl` and `orphans.msgpack`. Code has only 4 files (`src/Persistence/SaveModel.cs:24-32`). M2 deferred the rest (`docs/M2_STATUS.md:83`).
   - Companions were deliberately put in `player.msgpack` ("Reconciliation", `docs/PERSISTENCE.md:196`).
2. **The command log is "persisted for every save".** `docs/PERSISTENCE.md:296` and T-28 (`PERSISTENCE.md:602`) vs `CommandLogSha256 = null` always (`src/Persistence/SaveStore.cs:474`). There is no T-27 or T-28 test in the repo (grep).
3. **§6.2 chain table** stops at 10 -> 11 (`docs/PERSISTENCE.md:344-357`); the code has 12 steps (`src/Persistence/Migrations.cs:69-81`). **§5.1 says "Implemented so far (schema 7)"** (`PERSISTENCE.md:200`).
4. **`DATA_MODEL.md` §6 rule 1** says: "Additive changes with a default are migration-free — add the field, document the default, keep the same `schema_version`" (`docs/DATA_MODEL.md:850`). Actual doctrine and code: every additive field bumped the schema, and a missing field in the new schema is "corrupt, not defaulted" (`src/Persistence/SectionCodec.cs:399-409`; `tests/Persistence.Tests/Fixtures/README.md:77`; `docs/M6_STATUS.md:163`). **The implemented practice contradicts DATA_MODEL rule 1.** A schema-14 designer should follow the implemented practice, because the fixture tests enforce it.
5. **`dirty_reasons` `buildings` and `terrain`, and the cell `overrides` field** (`docs/PERSISTENCE.md:210-212`) do not exist in code (`src/World/WorldDelta.cs:489-494`; `docs/M2_STATUS.md:84`).
6. **§2's "Derived caches ... (§7.4 step g)"** (`docs/PERSISTENCE.md:86`) vs §7.4, where derived caches are step k (`PERSISTENCE.md:492`).
7. **`WorldLoaded` event** is specified (`docs/PERSISTENCE.md:497`; `docs/SYSTEMS.md:368`) but not implemented (`docs/M2_STATUS.md:85`; no symbol in `src/`).
8. **Instance-ID derivation.** `src/Domain/EntityId.cs:18` ("never derived from a seed"), `EntityId.cs:56` (`Create` "For fixtures and tests"), and AGENTS.md ("Instance IDs are `<prefix>_<ULID>` from `EntityId.NewId(kind)` or the registry; never build one by hand") vs `NpcSystem.InstanceIdOf` and `CreatureSystem.Identity`, which build IDs with `EntityId.Create` from a content hash (`src/World/Runtime/Social.cs` `InstanceIdOf`; `src/World/Runtime/Creatures.cs:160-165`). These are not seeded by the world seed, but they are content-derived and hand-built.
9. **Creature mind "transient" vs saved.** `src/World/Runtime/RuntimeState.cs:51` says "A creature's mind is transient", but schema 8 persists the mind (`src/World/WorldDelta.cs:154-161`, `177-184`; `docs/PERSISTENCE.md:246`). The comment is stale.
10. **Registry clock.** `docs/DATA_MODEL.md:846` lists "the registry's ULID anchor clock" as save-sensitive. No such clock is saved; IDs use the wall clock (`src/Domain/EntityId.cs:48-53`).
11. **Faction tier storage.** `docs/DATA_MODEL.md:150` puts "player reputation value/tier" in instance state, and SYSTEMS S-27 persists "Reputation values and tiers" (`docs/SYSTEMS.md:307`). But `DATA_MODEL.md:846` warns that "threshold changes retroactively reclassify a player", and PERSISTENCE §5.5 baseline-locks tuning (`docs/PERSISTENCE.md:276`). INFERENCE: storing tiers makes thresholds migration-locked. Tension, not a direct contradiction.
12. **Navigation ownership (owner ruling 1).** No document says Godot Navigation is authoritative, but several place the navmesh in a baking or streaming pipeline:
    - `docs/PERSISTENCE.md:83`: "Navmesh, collision ... | Baking / streaming pipeline";
    - `docs/WORLD_ARCHITECTURE.md:137`: "Collision, navmesh, occlusion | Baked";
    - `WORLD_ARCHITECTURE.md:414`: "Navmesh around buildings is rebuilt per cell on change, debounced";
    - `WORLD_ARCHITECTURE.md:435`: "Navmesh | Recast-style, baked per cell, stitched at cell borders", in §11 "Streaming, LOD, and the presentation contract";
    - `docs/ROADMAP.md:284`: "the navmesh updates on placement".

    INFERENCE: these read as presentation or baking artefacts. Ruling 1 requires a deterministic, headless, domain-owned derived structure rebuilt from authoritative state. **Flag for reconciliation.** The ruling itself was **not found** in the repo at `e10d2c4`, nor in the untracked `G:/UNNAMED/docs` files grepped (no "storey", "NavigationServer", "Godot Navigation" or "NavigationAgent" hits).
13. **One storey (owner ruling 2).** Not found recorded in the repo or the V4 handoff. Potential conflict: `docs/ROADMAP.md:280-281`, M7 building work lists "foundations, walls, floors, roofs, doors". "floors" is ambiguous (a ground floor vs upper floors). `DATA_MODEL.md:148` gives building-piece instance state a "transform". INFERENCE: under ruling 2, a piece transform needs no vertical placement freedom beyond the ground socket (x, z, yaw, socket).
14. **Party size.** `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md:93` says PERSISTENCE.md "still say[s] six active companions", but PERSISTENCE §9 now reads "player + up to 3 active" (`docs/PERSISTENCE.md:550`). The precedence doc is stale on this point (minor).
15. **`SaveTool` vs the game.** `SaveTool` has no transitions and no fixed nodes (`src/SaveTool/SaveToolApp.cs:62`, `150-158`). `GameSession` registers two transitions and fixed nodes (`src/Application/GameSession.cs:105-117`). The dry run is therefore not representative for real game saves (INFERENCE).
16. **Witness propagation.** SYSTEMS S-27 marks the "Witness-propagation working set" transient (`docs/SYSTEMS.md:308`). Owner ruling 3 ("information is not magically global") implies in-flight knowledge propagation matters. INFERENCE: if propagation is transient, a save/load mid-propagation either drops or instantly completes it; both are observable. The designer should decide whether pending propagation is persisted state.

---

## 11. Doctrine and constraints a schema-14 change must obey (FACT, cited)

- **Invariants I-1..I-9** (`docs/PERSISTENCE.md:29-39`). In particular:
  - store nothing derivable unless it diverges; definition IDs only, never content (I-1);
  - every persisted instance has a registry ULID (I-2);
  - every delta names its baseline (I-3);
  - atomic writes (I-5);
  - derived caches are computed once, after migration and alias resolution (I-6);
  - **every persisted record has a defined retirement path** (I-7, "A delta format without compaction is a leak with a schema");
  - slot-key merge (I-8);
  - randomness is derived, never sequenced; content identity is never an RNG input (I-9).
- **Ordered, one-step-per-version chain.** A gap refuses, and a version is never skipped (`docs/PERSISTENCE.md:342`; `src/Persistence/Migrations.cs:84-99`; the test at `tests/Persistence.Tests/MigrationTests.cs:424-429`).
- **Migrations are pure functions on in-memory sections.** No filesystem access, no definition-ID resolution, blockers rather than guesses (`src/Persistence/Migrations.cs:49-53`). A step throwing `InvalidOperationException`, `FormatException`, `ArgumentException` or `MessagePackSerializationException` becomes a blocker (`src/Persistence/SaveLoader.cs:116-125`).
- **Frozen shapes:** steps read version n's frozen shapes and write n+1's (`docs/PERSISTENCE.md:342`; `Fixtures/README.md:16-17`). The current codec "may change freely" only because every older shape is frozen (`src/Persistence/Sections/SchemaV1.cs:4-5`).
- **Fixtures:**
  - one per schema, written by that version's own writer, **never edited**, binary in git (`tests/Persistence.Tests/Fixtures/README.md:9-11`; `.gitattributes`);
  - `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture` fails until done (`Fixtures/README.md:19`);
  - "`expected.json` ... Never change it to make a failing migration pass" (`Fixtures/README.md:20-22`);
  - every fixture must both load complete (`HistoricalFixtureTests.cs:105`) and migrate through the real commit path to identical state, keeping the byte-exact `pre_migration_<schema>_quick` (`HistoricalFixtureTests.cs:292-316`).
- **Required fields are "corrupt, not defaulted"** in the current schema (`src/Persistence/SectionCodec.cs:399-409`). Only the migration step supplies defaults.
- **No silent backup fallback.** A corrupt manifest or player refuses with `SaveCorruptionException`, which lists available backups, "offered, never loaded automatically" (`src/Persistence/SaveModel.cs:152-159`; `src/Persistence/SaveLoader.cs:79-84`). `LoadBackup` is an explicit player choice (`src/Persistence/SaveStore.cs:201-207`). Corrupt `cells`/`entities` sections are **quarantined and reported**, never hidden (`SaveLoader.cs:85-93`, `410-430`). PERSISTENCE §7.2 extends this to `buildings`: "Load without it and report every lost structure explicitly; never fail the whole load" (`docs/PERSISTENCE.md:136`, `457-458`). A cross-section reference into a quarantined section is "reported loss, never re-quarantined" (`PERSISTENCE.md:252`, `461`).
- **A migration never commits lossy state silently.** `SaveStore.Migrate` refuses a save that loaded with quarantine, rejected records or a re-derived root (`src/Persistence/SaveStore.cs:260-263`). It commits through the same §7.1 sequence (`SaveStore.cs:265-266`). Loss is only by a declared rule (`report.Loss`), and the report is shown, never buried (M2b §14).
- **Never apply a delta to a different baseline.** Prove per cell, or rebase through a transition registered for exact fingerprints, or refuse (`docs/PERSISTENCE.md:388-415`; M2b §8).
- **Do not bump `save_format`** for section additions. A mismatch "refuses to load" every older save (`docs/PERSISTENCE.md:313`; `src/Persistence/SaveLoader.cs:58-59`). §3.2 already lists `buildings.msgpack` in format 1's layout, so adding it is arguably within format 1 (INFERENCE). The integrity code must be made schema-aware first (§0 item 2).
- **Architecture:**
  - Persistence may not write authoritative state and may not be `InternalsVisibleTo` Domain or World (`tests/Architecture.Tests/ArchitectureTests.cs:44-55`). It rebuilds the world only through the public static `WorldDelta.FromSnapshot`.
  - `WorldDelta` exposes no public mutation (`ArchitectureTests.cs:62-78`).
  - No static mutable state in the Domain, Registry, World or Persistence assemblies (`ArchitectureTests.cs:86-100`).
  - Only `src/Persistence` touches the filesystem (`docs/PERSISTENCE.md:17`).
- **Determinism:** canonical sorting, snake_case enum keys, integer units (mm or cm, millidegrees, ticks), byte-stable encoding (T-03, `docs/PERSISTENCE.md:577`). Integer positions only: "no floating point is persisted" (`src/World/PlayerState.cs:88-91`).
- **Definition-ID resolution** covers every stored ID, "never a guess" (`docs/PERSISTENCE.md:368-386`). The `quarantine` disposition (`orphans.msgpack`) is not implemented (`PERSISTENCE.md:382`; `docs/M2B_STATUS.md:132`).
- **No networking** (owner ruling 6; `docs/PERSISTENCE.md:27` "Out of scope in Phase 0: networking, cloud sync ..."; untracked V4 handoff line 131 "No networking in Phase 0–2", line 1514 "Do not build networking now").

---

## 12. Owner rulings: where recorded (persistence-relevant view)

| Ruling | Where found | Persistence-relevant conflict or tension |
|---|---|---|
| 1. Navigation deterministic, headless, domain-owned, rebuilt from structure state, across seams | **Not found** in the repo at `e10d2c4` or in the untracked docs grepped | §10 item 12: `PERSISTENCE.md:83`, `WORLD_ARCHITECTURE.md:137/414/435`, `ROADMAP.md:284` describe baked navmesh. Otherwise consistent with "not saved, rebuilt" |
| 2. Building v1 is one storey | **Not found** | `ROADMAP.md:280-281` "floors, roofs" (ambiguous); `WORLD_ARCHITECTURE.md:409` footprint "one or more cells" (fine) |
| 3. Factions minimal; axes kept separate; the same act affects two factions differently; no global information | `docs/SYSTEMS.md:306-310` (S-27: "It never decides who attacks ... derived from faction relation, war state, legal status, identity knowledge and perception, not from a standing tier"); `docs/ROADMAP.md:280` ("no universal morality meter", "the same act moves two factions in opposite directions in a fixture", `ROADMAP.md:284`); untracked V4 handoff lines 1404-1413 (the keep-separate list) | `DATA_MODEL.md:150` and SYSTEMS S-27 "values and tiers" (§10 item 11); S-27 witness propagation transient (§10 item 16); existing `RelationshipValue` dimensions `affection, fear, grudge, respect, trust` in [-100, 100], zero never stored (`src/Domain/Social/Social.cs:32-39`; `src/World/PlayerState.cs:37-38`) already hold per-NPC trust, fear and grudge in the save |
| 4. C10 retired | `docs/PROTOTYPE.md:298` ("Revised by owner ruling (2026-09-24)"); `docs/M6_STATUS.md` Phase-1 closeout | none for persistence |
| 5. No core action requires a radial | Untracked V4 handoff lines 78-82 | none for persistence |
| 6. No networking in M7 | `docs/PERSISTENCE.md:27`; untracked V4 lines 131 and 1514 | none |
| 7. Engine-independent C#, dotted definition IDs, ULIDs, sparse deltas | `AGENTS.md`; `docs/PERSISTENCE.md:6`; untracked V4 lines 120-121 | §10 item 8 (content-derived IDs built by hand) |

---

## 13. Design options for M7 schema 14 (all INFERENCE, for the designer)

**Structures (building v1):**
- *Option S1: a `structures` list inside `entities.msgpack`.* This is the schema-6 and schema-8 pattern: no new file, no integrity-root change, quarantined together with entities.
  - Record: `instance_id` (`bld_`), `def_id` (a structure or blueprint kind), `host_cell` plus `baseline_hash` (in `baselines`), `owner` (ULID or `chr_`), absolute-mm anchor and yaw, and pieces `[{piece_id, def_id, socket_path, health}]`.
  - Freeze the schema-9..13 entities shape and repoint `SchemaV8ToV9`.
  - Downside: a corrupt entities section loses structures *and* containers and creatures together, which contradicts §3.3's separate-quarantine intent (`docs/PERSISTENCE.md:136`).
- *Option S2: a new `buildings.msgpack`,* as in the doc layout.
  - Needs schema-aware `CheckedFiles` (and schema-aware reading at `SaveLoader.cs:96`); the migration step would create an empty section; and `SaveStore.Commit` must write it.
  - Gains independent quarantine and the doc's "report every lost structure" behaviour.
- In either case:
  - pieces are rows keyed by ULID or by `structure_id + socket_path` (no piece prefix exists);
  - storage containers referenced by ULID follow the §5.4 cross-section rule (`docs/PERSISTENCE.md:252`);
  - a seam-straddling structure proves every covered cell's hash;
  - the retirement path (I-7) is demolition, which deletes the record and retires its identities;
  - navigation and collision are rebuilt in the `Simulation` constructor, never saved;
  - piece health is per piece, explicit (D-08).

**Factions:**
- *Player-scoped* (reputation per faction as a raw value, crime/bounty records, pardons, known-identity records): a `PlayerDto` field or fields (the `relationships` pattern, schema 10). Faction IDs go through the def-ID pass; tiers are derived at run time.
- *World-scoped* (faction-to-faction attitude overrides, war state, NPC faction membership changes, witness knowledge): there is no current home. Choices:
  - (a) cell flags anchored to a settlement cell. This is proven by `baseline_hash` but awkward for global facts;
  - (b) a new world-global record list, for example `world_state` in the entities section or a new section. It has no baseline proof, needs its own retirement rule, and must be added to `DeltaSnapshot` (or a sibling), `StateDump`, `CanonicalState` and the digests;
  - (c) keep it in the player section (Phase-2 is single-player; ruling 6 keeps it seam-only). Simplest, but it mixes world facts into the character (PERSISTENCE §5.1 already lists "faction reputation and crime records" there, `docs/PERSISTENCE.md:188`).
- The "same act moves two factions in opposite directions" fixture (`docs/ROADMAP.md:284`) should become part of the v14 historical fixture world, so the migration and round-trip tests cover it.

---

## 14. Open questions

1. New section file or new list in `entities.msgpack` for structures? (Integrity-root consequence: §0 item 2.)
2. Where does world-global faction state live? There is no global world record today (§4.2, §13).
3. The piece identity scheme: no piece prefix exists in `EntityKind` or `DATA_MODEL.md` §2.2.
4. The coordinate convention for structures: cell-relative cm (like created instances) or absolute mm (like creatures and companions)? How are straddling structures proven, and against which cells?
5. Is pending witness or knowledge propagation persisted, or completed or discarded at save? (Ruling 3 vs S-27 "transient".)
6. Store reputation tiers, or derive them? (Storing them makes thresholds migration-locked.)
7. Should NPC assignment and faction membership reference `npc.*` definition IDs (the def-ID pass covers them) or instance ULIDs (dangling-reference validation is unimplemented)?
8. Will M7 change placement data (fixed nodes, populations)? If so, freeze the M6 fingerprint constant and register an "M7" transition in `GameSession.Boot`.
9. Should `SaveTool` learn the game's transitions and fixed nodes, so `save:migrate --dry-run` is meaningful on real saves?
10. Does M7 implement the specified-but-missing `WorldLoaded` event and step-m invariant validation (structures reference containers and NPCs, which makes dangling references possible)?
11. Should the §6.2 chain table and the stale §5.1 "schema 7" text be fixed in the same change as schema 14?
