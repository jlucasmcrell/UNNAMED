# Final code-fact spot check: M7_IMPLEMENTATION_DESIGN.md against e10d2c4

Source: the read-only snapshot at `scratchpad/main_e10d2c4` (origin/main `e10d2c4`). Design: `G:\UNNAMED_HISTORY\M7_DESIGN_2026-09-24\M7_IMPLEMENTATION_DESIGN.md` (7,522 lines). Settled rulings in `drafts/00_SCOPE_RULINGS.md` and `drafts/01_LEAD_RULINGS_ON_AUDITS.md` are not reported.

**Method.** I pulled 68 load-bearing claims about existing code from sections 2, 3, 4, 5, 7 and 8. A few §6 and §13 claims were included where the task named them: composition, Dispatch, `SystemContext` helpers, `ClosedDoors` call sites, Playthrough legs and Defend. Every claim was checked at its cited line in the snapshot. Numeric claims that depend on content were recomputed: `TalkReachMm`, tier bands, pad relief, seat distances, ware refs, prices, HelpPanel row counts and dump leaf counts. The pad relief was recomputed with a Python copy of `TerrainGrid.HeightAtMm`.

**Result.** 57 of the 68 claims hold exactly. The 11 that do not are findings 1-12 below. Finding 1 is not a stale citation: the design gives one field two writers, and the existing slice-ownership check throws when the second writer writes. Finding 2 specifies a test that cannot compile against the existing access modifiers. The rest are stale or partly false descriptions and one internal numbering slip.

---

## Findings (most severe first)

### 1. HIGH: `StructureAudit` is owned by `Structures`, but `NpcSystem.Populate` also writes it

- **Design, line 1464:** "**Derived state** (owned by `StateSlice.Structures`, written only through `RuntimeState` wrappers, rebuilt by `Rebuild()`)". The table's row at **line 1470** is `StructureAudit` … "`Populate` only".
- **Design, lines 1835 / 3141 / 5631 / 961:** "`Populate(companions)` places an errand NPC at the errand pose … and repairs, each with a `StructureAudit` line". Line 3141: "`_npcs.Populate(player.Companions); // … drops an errand whose NPC is a saved companion (StructureAudit line)`".
- **Design, line 3686:** "| `StructureAudit` | `BuildingSystem`, `NpcSystem` | the two `Populate`s | a report |".
- **Source, `src/World/Runtime/RuntimeState.cs:329-333`:** `Require(owner, slice)` throws `"{owner.System} wrote {slice}, which {…} owns"` when a system writes a slice it did not claim. §6.1.2 has `NpcSystem` claiming only `Npcs` and `NpcErrands`. §6.1.9 (line 3280) gives `SetStructureDerived(owner, space, closedLeaves, socketIndex, footprints)` no audit parameter and no errand-side writer.
- **Failure:** implemented as written, the first load with an errand repair throws "NpcSystem wrote Structures, which BuildingSystem owns". The only other way is for the implementer to invent a second writer path.
- **Fix (rule):** split the audit by owner. `BuildingSystem.Populate` writes the piece lines through `SetStructureAudit(owner, lines)`, which `Require`s `StateSlice.Structures`. `NpcSystem.Populate` writes the errand lines through a new `SetErrandAudit(owner, lines)`, which `Require`s `StateSlice.NpcErrands`. `Simulation.StructureAudit` returns the building lines, then the errand lines in `NpcId` order. Line 1470's row should read: "`StructureAudit`: piece lines on `Structures` (`BuildingSystem.Populate`); errand lines on `NpcErrands` (`NpcSystem.Populate`); the view concatenates them". Line 3686's owner column should read: "`BuildingSystem` (piece lines, `Structures`); `NpcSystem` (errand lines, `NpcErrands`)".

### 2. MEDIUM: G11 calls `SemanticRebase.Apply` and the definition pass directly, but both are internal to Persistence, which has no `InternalsVisibleTo`

- **Design, line 3836:** "Guard G11 … encodes and decodes, runs the pass under a content identity whose hash differs, runs `SemanticRebase.Apply` over every host cell with the fixture generator on both sides, and asserts every property equal element by element". The test lives in Persistence.Tests (lines 3546, 6229).
- **Source:**
  - `src/Persistence/BaselineTransitions.cs:31` declares `internal static class SemanticRebase`.
  - `src/Persistence/SaveLoader.cs:23` declares `internal static class SaveLoader`, and `ResolveDefinitions` at `:206` is `private static`.
  - `src/Persistence/Persistence.csproj` has no `InternalsVisibleTo`. The only ones in `src/` are `World.csproj:19-21` (World.Tests, Persistence.Tests, M2.Probe) and `Domain.csproj:14`.
  - No existing test references either type (grep over `tests/`).
- **Failure:** the G11 test does not compile. Driving it only through `SaveStore.Load` would not run the rebase "with the fixture generator on both sides", because no transition fires.
- **Fix (rule):** in E2 commit 3, add `<InternalsVisibleTo Include="Persistence.Tests" />` to `src/Persistence/Persistence.csproj` and change `SaveLoader.ResolveDefinitions` from `private` to `internal`. List both edits in §6.5.2's Persistence row and in §7.17's E2 commit list.

### 3. MEDIUM: §2.18's "only permitted Phase-1 test edits" list is stale against §3.20, §4.20, §9.9 and §12.10

- **Design, line 477:** "Existing tests named elsewhere as edited are the only permitted Phase-1 test edits: `LoadAll_Loads_Yaml_Files` …, `TheSimulation_ExposesOnlyReadsAndTheCommandPath` …, `WorldDelta_ExposesNoPublicMutation` …, `AViewThatListensToEverything_…`, the `MigrationTests` step lists and deconstructions, `TheWritersContentIdentity_IsItsFixturePack` …, `HistoricalFixtureTests`, `CanonicalState`, `ASaveAndALoad_CompareEqual_FieldByField` (leaf count), and the `DialogueRulesTests` fake."
- **Design, lines 1238 / 6330:** "`HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame` (`CompanionTests.cs:285`) gains `Assert.Equal(before.Route, after.Route)`".
- **Design, lines 6327 / 5166:** `TheProbesContentMirror_IsTheFixtureContentPack` is edited (0.2.9, and 0.2.10 if the pack is bumped).
- **Design, lines 1930 / 4818 / 6332:** "`KnownDirectories_Is_Closed_Set` may gain `pieces`".
- **Source:** all three tests exist: `tests/Application.Tests/CompanionTests.cs:285`, `tests/Persistence.Tests/MigrationTests.cs:735`, `tests/Content.Tests/ValidationTests.cs:332`.
- **Failure:** an implementer who reads §2.18 as the closed list hits STOP S1 at E2 or E4 on a planned edit.
- **Fix (text):** replace line 477's sentence with "The only permitted Phase-1 test edits are §12.10's 'Edited by name' list". Otherwise, append `HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame` (route assertion, E4), `TheProbesContentMirror_IsTheFixtureContentPack` (0.2.9 / 0.2.10) and `KnownDirectories_Is_Closed_Set` (may gain `pieces`).

### 4. LOW: the `QuestSystem` citation is stale, and "runs last" is wrong

- **Design, line 359:** "| `QuestSystem` | `src/World/Runtime/Quests.cs:62`, `:118-137` | Runs last; deed fan-in `RecordDeed` |".
- **Source:** `Quests.cs:62` is a blank line. `RecordDeed` is declared at `:61`, `class QuestSystem` at `:70`, `Handle(RecordDeed)` at `:101-116` and `Tick` at `:118-137`. `Simulation.cs:332-333` runs `_quests.Tick` and then `_clock.Tick()`, and line 248 of the design itself says "clock (last)".
- **Fix:** "`src/World/Runtime/Quests.cs:70` (class), `:101-116` (`Handle(RecordDeed)`), `:118-137` (`Tick`) | Runs after every other system except the clock; deed fan-in `RecordDeed` (`:61`)".

### 5. LOW: `QuestDebugger.DescribeCondition` is not a closed switch

- **Design, line 361:** "| `QuestDebugger` | `src/World/Runtime/QuestDebugger.cs:372` | `DescribeCondition` closed switch |". Line 351 correctly calls `DialogueRules.Holds` a closed switch; that one throws on an unknown condition.
- **Source:** `QuestDebugger.cs:386` is `_ => condition.ToString(),`. A condition without an arm falls back silently to the record's `ToString`. It neither throws nor fails to compile.
- **Failure:** a missing `reputation` or `act_done` arm would go unnoticed unless a test asserts the §5.7.1 wording.
- **Fix:** "`DescribeCondition` switch with a `ToString()` fallback (`:386`); the M7 arms must be pinned by a test asserting §5.7.1's two sentences, for example `DescribeCondition_NamesStandingAndActDone` in E3".

### 6. LOW: `QuestDebugger.Recipe` reads `Setup.Layout.Stations`, but the "complete list" of Phase-1 touches does not mention it

- **Design, line 216:** "The 'M7 touch' column is normative: it is the complete list of Phase-1 code M7 changes". Line 361, and the `QuestDebugger` part of the §6.5.2 row at line 3487, touch only `DescribeCondition`. §4.14 (line 1757) says `Stations()` returns authored then piece stations and lists `CraftingSystem` and presentation as its readers.
- **Source:** `src/World/Runtime/QuestDebugger.cs:392`: `var station = Setup.Layout.Stations.Where(s => s.Kind == recipe.StationKind)…`.
- **Failure:** F4's recipe line names only the authored smithy anvil after the player builds a bench. Nothing breaks, but the "complete list" claim is false.
- **Fix (pick one and state it):** add a MINIMAL row, "`QuestDebugger.Recipe` (`QuestDebugger.cs:392`) reads `_context.Stations()`". Or record it as unchanged: "`QuestDebugger.Recipe` keeps `Setup.Layout.Stations` (authored stations only), NONE".

### 7. LOW: not every scripted mode is "fixed seed, one tick per frame"

- **Design, line 417:** "| Scripted modes | `Smoke.cs`; `Playthrough.cs:38`, `:71`; `DeltaShots.cs:43`; `UiShots.cs` | Fixed seed, one tick per frame, transcripts, `StateDump` JSON |".
- **Source:**
  - `Main.cs:103` passes `Playthrough.Seed` only for `--playthrough` and `--delta-shots`. `--smoke` and `--ui-shots` start with seed 0, which is random.
  - `Main.cs:305` steps one tick per frame only when `_smoke`, `_play` or `_delta` is set. `--ui-shots` (`_shots`) steps by real frame time.
- **Fix:** "`--playthrough` and `--delta-shots`: fixed seed (`Main.cs:103`), one tick per frame (`:305`), transcripts and `StateDump` JSON. `--smoke`: random seed, one tick per frame. `--ui-shots`: random seed, real-time frames".

### 8. LOW: `Trade` is not dispatched only by `Handle(BuyCommand)`

- **Design, line 2664:** "`Handle(BuyCommand)` is the only path that moves a trader's wares: `Trade` is dispatched only from there".
- **Source:** `src/World/Runtime/Social.cs:515`. `TradeSystem.Handle(SellCommand)` also dispatches `Trade`, carried → wares. `:496` is the Buy dispatch.
- **Fix:** "`Trade` is dispatched only by `TradeSystem.Handle(BuyCommand)` (`Social.cs:496`) and `Handle(SellCommand)` (`:515`); only the Buy path moves a stack out of a trader's wares, and `InventorySystem.Check` refuses `MoveItem` and `TakeAll` on wares". The conclusion, that the gate cannot be bypassed, still holds.

### 9. LOW: §8.9 gives talk reach the wrong assign-refusal number

- **Design, line 4372:** "**Asking is in person.** Focus uses talk reach, which is assign refusal 5."
- **Design, lines 1794-1795:** "4. the player's body is not within `TalkReachMm` (1950) of the NPC's body …; 5. the piece is not an intact station".
- **Fix:** line 4372 should say "…which is assign refusal 4."

### 10. LOW: World.Tests does not "load content through `TestWorlds.cs`" in any way N-A5 can use

- **Design, line 1153:** "`tests/World.Tests/NavigationRuntimeTests.cs` (internals, `src/World/World.csproj:19`; World.Tests loads content through `TestWorlds.cs`)". N-A5 (line 1202) is a World.Tests test on "real content": a running `Simulation` with Tavar, the fold barrier and `NpcSystem`.
- **Source:** `tests/World.Tests/TestWorlds.cs` only hashes a content root (`ContentHashOf`, `:52-55`). It builds no `SimulationSetup`. World.Tests references Content and World but not Application (`World.Tests.csproj:22-25`), so `GameSession.Boot` and `Harness.Boot` are not available.
- **Fix:** add to §3.20: "E1 adds `TestWorlds.HollowSetup()`, which builds `SimulationSetup` from `content/` with the same Content builders as `GameSession.Boot` (`src/Application/GameSession.cs:86-99`)". Alternatively, move N-A5 to Application.Tests on `Harness.Boot` and inject the errand with the public `WorldDelta.FromSnapshot`.

### 11. LOW: the raw dump does check trail and route order

- **Design, line 441:** "`StateDump`'s replayable mode sorts arrays by content, so trail and route order are checked only by digests."
- **Design, line 3926:** "…route corners and trail marks are order-checked only by the raw dump and the digests." This is the correct statement.
- **Source:** `src/Application/StateDump.cs:35-44` sorts only `if (replayable)`. The raw dump that `ASaveAndALoad_CompareEqual_FieldByField` compares keeps array order.
- **Fix:** line 441 should read "…so in replayable comparisons trail and route order are checked only by the raw dump and the digests."

### 12. LOW: the F6 excerpt records the armour's post as the act position

- **Design, line 4466:** "#2 creature_killed creature.construct.animated_armour  r_0_0:c_00_00 (65.0, 34.0) tick 9120".
- **Design, line 2333:** "**The position is the actor's body, never the effect's.**" Line 6524 has the `m7_armour` beat stand 1.8 m behind the sentinel. `content/spawns/hollow/iron_shelf_armour.yaml` puts the post at (65, 34) with radius 0.
- **Fix:** make the excerpt's position the player's body, not the post. For example "(63.3, 33.4)", marked "illustrative: the player's body 1.8 m behind the armour's post".

---

## Claims verified true (57 of 68)

| # | Doc line | Claim | Verified at (snapshot) |
|---|---|---|---|
| 1 | 247 | Constructor `:100-145`; 21 systems plus `QuestDebugger` at `:116-137`; `RequireEverySliceOwned` `:139`; Seed, Populate×3, Settle order | `Simulation.cs:100-145` |
| 2 | 248, 3319 | Step order: movement … quests, clock | `Simulation.cs:312-339` |
| 3 | 249 | `Queue<GameCommand>` `:72`; `DrainCommands` `:268-306`; `LoggedCommand` `Commands.cs:29`; 23 command types; rejection string plus `CommandRejected` | as cited |
| 4 | 250, 3189 | `Dispatch` `:369-404`, 32 arms, an unknown type throws; `Now` `:367` | as cited |
| 5 | 251, 3096 | 18 slices `:19-74`; `Claim`/`RequireEverySliceOwned` `:144-161`; `Require` `:329-333` | `RuntimeState.cs` |
| 6 | 252 | `Posture = player.Posture` at `:102`, in the constructor `:97-115` | `RuntimeState.cs` |
| 7 | 253, 3238 | `SystemContext` `:24-87`; `ClosedDoors` `:48`; `FindContainer` `:51-52` (authored → corpse → merchant); `WaresOf` `:61-64`; `TalkReachMm` `:67` = 1600 + 350 = 1950; `Obstacles` `:82-86` excludes only companions | `Systems.cs` |
| 8 | 254 | `SimulationSetup` `:14-33`; init properties default to `XSetup.Empty` | `Simulation.cs` |
| 9 | 255 | `Aim` `:202`; `DynamicBlockers` `:255`; `CaptureRecord` `:342-351`; `StateDigest` `:357-364`, tag `unnamed.simulation/v1` | `Simulation.cs` |
| 10 | 256 | Exact-type publish in subscription order | `EventBus.cs:24`, `:50` |
| 11 | 258 | Frame clamp 0.25 s (`:52`); drain, step, drain; 300 s autosave | `GameSession.cs:177-201`; `SaveStore.cs:39-45` |
| 12 | 265, 1352 | `Step` `:119`/`:129-160`; sub-steps ≤ r/2; ≤ 4 passes, statics then dynamics; tuck `:192`; Y = terrain + lift `:157-159` | `Kinematics.cs` |
| 13 | 266, 565 | Box/Circle shapes `:41`/`:95`; touching is clear (`:52`, `:102`) | `Blockers.cs` |
| 14 | 268, 699 | `IsClear` height-blind, bounds `:171-172`; `CanStand` reads only clearance > 0 | `Kinematics.cs:170-177` |
| 15 | 269 | 17 `Layout.Space` reads (13 collision or sight, 4 terrain); the 12 switch lines, `Creatures.cs:173` and the four terrain lines are exact | grep over `src/World/Runtime` |
| 16 | 270 | 41 × 41 grid at 5 m, `ashen_hollow.yaml:15-62`; integer `HeightAtMm` | `TerrainGrid.cs:51` |
| 17 | 271 | `DoorSite`…`NpcSite` `:12-49`; `ContainerSite(Key, LootTableId, XMm, ZMm, StackSlots)`; `StationSite(Key, Kind, XMm, ZMm)`; `WalkSpace` `:63` | `RegionLayout.cs` |
| 18 | 272 | `ClosedDoors` = closed doors then standing barriers (`:94-97`); a flag lives in the cell of the footprint centre | `RegionLayout.cs`; `Simulation.cs:169-183` |
| 19 | 273, 2326, 2343 | `InteractionSystem` `:240-293`; close refused only for the player (`:268`); a switch sets once (`:283-284`); `SetWorldFlag` `:288`; `SwitchSet` `:290` | `Systems.cs` |
| 20 | 274, 957 | A 140/160 m band (full 150 ± hysteresis 10); `Settle` `:471`; B and C stubs `:521`; `Tiers.cs:36-47` | `simulation_tiers.yaml`; `Systems.cs`; `Tiers.cs` |
| 21 | 275 | `Sees`: range, Sin/Cos field of view, `Crosses` | `Perception.cs:87-107` |
| 22 | 281, 530 | `Follow` `:325-361`; catch-up at > 30 m or 80 ticks (`snag_s: 4`); headway ≥ 30% | `Companions.cs`; `companion.yaml:10-11` |
| 23 | 282, 535 | Trail 1 m, 48 marks (`:308-316`); an order clears the trail (`:186-187`) | `Companions.cs` |
| 24 | 284, 794 | Obstacles `:570-581`: closed doors, living creatures, the player, other NPCs | `Companions.cs` |
| 25 | 285, 3244 | Clear view `:584-595`; `Walled` `:597-598`; the other `Walled` copies at `Combat.cs:569-570` and `Creatures.cs:893` | as cited |
| 26 | 287-288, 858 | `Recruit` `:160-171` idempotent; `Populate` `:125-143`; `Records` `:145-156` | `Companions.cs` |
| 27 | 289 | `CompanionRecord` `:52-62` | `PlayerState.cs` |
| 28 | 290-291, 954 | `NpcSystem`: 18,000 mdeg/tick; facing loop `:149-164`; `PlaceNpc` `:141-147`; `NpcState` `:88`; ID `:121-125` | `Social.cs` |
| 29 | 292 | `NpcDefinition(Id, Name, Role, Services, MerchantId, DialogueId)` `{ Companion }`; no faction field | `Domain/Social/Social.cs:10-16` |
| 30 | 298 | Spawn homes: 16 samples on channel `spawn`, `IsClear` against `Layout.Space` and earlier homes; homes recomputed on load | `Creatures.cs:171-225` |
| 31 | 302, 2315 | `Die` `:701-714`; `RecordDeed` in the unbraced `if (killer == _player)` at `:705-706` | `Creatures.cs` |
| 32 | 310-313 | Melee sweep `:495-507` (`AttackMissed` at the window's last tick); `Loose`/`Trace` `:511-545`, 10 mm bisection; `Magic.cs:114` calls `Loose` | `Combat.cs`; `Magic.cs` |
| 33 | 321-323, 1658 | `ExchangeItems` `:91`/`:327-357`; `GrantItem` `:106`/`:367-374` (ground fallback); `Put` `:569-609`, `ItemId` merge order at `:576`/`:600`; `MergeInto` `:611-627` | `Items.cs` |
| 34 | 325-328, 1740 | `Check` `:450-460`; `Baseline` `:494-510`; `Materialize` `:513-523`; corpse clause `:553-562` (`if` at `:558`) | `Items.cs` |
| 35 | 329-330 | `RemoveContainer` retires every ID; `PlaceItem` keeps the ID and touches no registry | `WorldDelta.cs:390-400`, `:360-367` |
| 36 | 339-341, 1757 | Station check `:168-170`; dead/busy `:158-161`; spend order `:178`; channel `craft` `:192`; `march_spear.yaml:8` names `anvil` | `Crafting.cs` |
| 37 | 349-352 | `DialogueSystem` `:206`; `TalkCommand` `:225-246`; `SpeakerFacts` `:432-459`; `TradeSystem` `:468-551`; Buy needs no conversation | `Social.cs` |
| 38 | 351, 2548 | `IDialogueFacts`: 8 members; three implementers (`Social.cs:206`, `:432`, `DialogueRulesTests.cs:12`); `Holds` `:148-159` throws on an unknown condition | grep |
| 39 | 353, 2542 | `MerchantStock(ItemId, Count, PriceBias)` `:220`; `BuyPrice = ceil(value × bias)` (billet 20) | `Domain/Items/Items.cs:220`, `:227` |
| 40 | 360, 2543 | `NotBuilt` `:141-160` (`faction_*` at `:154-155`); reward `reputation` not built `:205-206` | `Domain/Quests/Quests.cs` |
| 41 | 368-370, 1909 | `EntityId` `:46`/`:48-53`/`:58`/`:129`; 12 kinds; inference only for item, creature, npc and quest; registry throws on a seen ID and only tombstones on destroy | `EntityId.cs`; `EntityKind.cs`; `EntityRegistry.cs:44-66`, `:133-142` |
| 42 | 377-378, 3802 | Schema 13 at `SaveModel.cs:20`; 12 steps at `Migrations.cs:69-80`; the step is pure (`:49-53`) | as cited |
| 43 | 3574, 3739-3742 | Repoint sites: `:566` (V8→V9 `new EntitiesSectionDto`), `:701` (V11→V12 `Array.Empty<CompanionDto>()`), `:722` (V12→V13 `new PlayerDto`), `SchemaV12.cs:32`; header `:4-7`; `SchemaV8.cs:46` | as cited |
| 44 | 380, 3590, 3731-3733 | `PlayerDto` `:21-63` (18 keys); `CompanionDto` `:75-90` (11 keys); `EntitiesSectionDto` `:203-218`; `Prove(cell, hash, EntityId)` `:481`; 4-tuple `DecodeEntitySection` `:555`; `DecodeEntities` `:598` | `SectionCodec.cs` |
| 45 | 381, 3703-3708, 3817 | Decode `:142-155`; `SaveCorruptionException` `:147-155`; pass only on a hash change `:158-159`; `ResolveDefinitions` `:206-359`; `ProveBaselines` `:361-408`; quarantine `:410-430` catches `ArgumentException`; hand-built snapshots `:146`, `:358`; `TryReadRoot` `:469` | `SaveLoader.cs` |
| 46 | 3827-3838 | Exactly four `DeltaSnapshot` construction sites in `src/`; `WorldDeltaTests.cs:194` positional; seven `With*` at `PlayerState.cs:222-253` hand-list `Posture`; the pass ends at `SaveLoader.cs:356-357` | grep |
| 47 | 384, 3656, 3896 | `Posture` init validator `:306-317`; digest `unnamed.player/v9` at `:328`; companion loop `:198-205` | `PlayerState.cs` |
| 48 | 385, 3926 | `Render` `:25-46`; mask regex `:93-94`; `Order` sorts object arrays `:97-120`; `Named` gives `pce#N` | `StateDump.cs` |
| 49 | 386, 4010, 4052 | 0.2.8 at `HistoricalFixtureTests.cs:16`; alias arm `>= 12` at `:241`; `M2Fixtures.cs:109-110`, `:252-253`, `:259-283`; `MigrationTests` `:78`, `:104-262`, `:398`, `:428`, `:735-745`, `:751-757`; deconstructions only at `:148`, `:179`, `:197` | as cited |
| 50 | 394-395, 3293 | 28 kind entries; `faction` registered at `:158-164` (directory `factions`); `faction_ref` in the suffix table `:18-44`; an unknown `_ref` is XREF004 (`:132-135`) | `SchemaResolution.cs`; `ContentChecks.cs` |
| 51 | 396, 2625-2627 | Conditions `:26-27`; consequences `:29-30`; five NPC fields refused; `not` only on four kinds | `SocialContent.cs:26-30`, `:159-163` |
| 52 | 407, 4228 | `DefineInput` `:1061-1104`; `PhysicalKeycode`; B, T, Y, Z, Delete, PageUp, PageDown, F2 and F6 unbound | `Main.cs` |
| 53 | 412, 4398 | `const MaxDistance = 6f` `:23`; `Zoom` clamps `:85`; `PerfRun.cs:56` reads it statically; `_restoreDistance` exists; pitch clamp −1.35 rad (8.8 m above the eye at 9 m) | `CameraRig.cs` |
| 54 | 415, 4486-4502 | `Sections` `:14-40`, `Developer` `:42-45`, `LeftSections = 3` `:48`; after M7 the left column is 30 rows and the right 20 | `HelpPanel.cs` |
| 55 | 4222-4514 | `Main.cs` 88-91, 103, 297-300, 305, 342, 374-383, 385-387, 403-407, 461-462, 489, 498-507, 514-538, 567, 571, 582-583, 604-606, 642-646, 662-670, 778-784, 1003-1007, 1021-1031, 1045-1047 | `Main.cs` |
| 56 | 2604-2605, 6507, 6515, 6518 | Legs `:64-67`, `HomeWithTavar` `:68`, `HomeToTheSmithy` `:58-59`; `follow` `:213`, `ward` `:214`; `Record()` `:534-573`; `Defend()` skips `sentinel`/`stray` at `:614`; `Seed` `:38`; `Armed` `CreatureTests.cs:36-44`; `TheArmoursOpenBack` `:303-311` | `Playthrough.cs`; `CreatureTests.cs` |
| 57 | 729, 3431-3435 | Arrays count as mutable (`ArchitectureTests.cs:204-209`); allow-list test `:111-131` ignores property getters; presentation scan `:162-183`. "True today": no `.Subscribe(` in World or Domain, no physics-query names in Presentation, Godot navigation only in `Spike/`, no `ImmutableCollectionsMarshal` anywhere | grep |

Content-derived numbers recomputed and correct:
- door lanes: `ashen_hollow.yaml:143-144`, fence `:80`, beam `:89-91`;
- pad relief: worst 228 mm at (111, 105), square (33, 33) 96 mm;
- build-area clearances: `tree_25` 0.4 m, valley strays 14.56 m, Sel 17 m;
- seat distances: 20.6, 8.5 and 28.6 m, and Kera 61.8 m at the bench;
- ware refs `#00`-`#05`;
- walk 80 mm and run 160 mm a tick;
- `StateDump` leaf counts: 10 per route, 10 per piece, 7 per act and knowledge row, 2 per standing row, and 37 for the P5 ledger beyond a new game.
