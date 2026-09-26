# M7 design audit: existing-code facts

Auditor: code-fact verifier, 2026-09-24. Base: origin/main `e10d2c4`, read-only snapshot `main_e10d2c4`. Every `path:line` below is repo-relative and was re-read in the snapshot for this audit. Drafts audited: `00_SCOPE_RULINGS.md`, `A_navigation.md`, `B_building.md`, `C_factions.md`, `D_cross_system.md`, `F_persistence.md`, `GH_content_ui.md`, `IJ_risk_perf.md`, `E_slices_tests.md`. This audit rewrites nothing. Each finding says what the draft claims, what the code says, and how to fix the draft.

## 1. Method and overall verdict

**Mechanical pass.** A script pulled every `file.cs|yaml|json:line` citation out of the nine drafts: 859 citation instances and 526 distinct targets. It resolved each against the snapshot and checked that the cited line exists. One citation points past the end of its file (A N-A10, `tests/Application.Tests/CreatureTests.cs:526-544`; the file has 543 lines). Every other citation lands on a real line.

**Semantic pass.** The 68 most load-bearing claims about existing code were re-read against the source, not against the research notes. These cover movement and collision, the blocker sets, IDs and the registry, slices and composition, tick order, dispatch, containers and corpses, trade, perception, the persistence codec, loader, rebase and migrations, the digests and `StateDump`, content loading and lint, and the architecture tests. The ledger is in §4.

**Verdict.** The drafts are unusually accurate. About 95% of the load-bearing claims are exactly right, and most of the rest are citation drift. Two claims are wrong in a way that would stop a slice on the first run:
- **C's faction guard test would fail on today's code.** It bans the token `TierOf`, which `Creatures.cs` and `Companions.cs` already use as their simulation-tier helper (F1).
- **A's lint NAV006 and test N-A1 would reject the shipped content.** Two authored location anchors sit inside authored rocks (F2).

Four more claims would each break compilation or leave a guard with nothing to test (F3-F7). No draft presents a new API as though it already exists, except one wording slip (F16). `EntityId.Derived`, `EntityKind.Piece`, `SystemContext.Space`/`SightWalls`/`Stations`, `Simulation.Space`/`PreviewPlacement`, every `Nav*` type, `FactionLedger` and the `StateSlice` additions are all correctly labelled as new. A grep confirms that none of their names exist in `src` today.

---

## 2. Findings, most severe first

### F1 (HIGH): the faction guard test is red on unmodified code (C §12; D29 and G6; E3, E5 and E9 test rows; IJ R-C1)

**Claim.** C §12 (lines 635-638): `TacticalCode_NeverReadsFactionState` scans `Combat.cs`, `Creatures.cs`, `Companions.cs` and `Magic.cs`. It "fails on any of: `State.Factions`, `StandingOf`, `Setup.Factions`, `FactionRules`, `TierOf`, `StandingLevel`", and "the `new RecordAct(` dispatch in `Creatures.cs` contains none of them, so it passes". D29 and G6 extend the scan to `Navigation.cs`, `Building.cs`, `BuildingRules.cs` and `Errands.cs`.

**Code.** `TierOf` is already the simulation-tier helper in two of the scanned files:
- `src/World/Runtime/Creatures.cs:264` (`if (TierOf(c.Body) != SimulationTier.A)`) and `:897` (`private SimulationTier TierOf(Body body)`);
- `src/World/Runtime/Companions.cs:263`, `:423` and `:626`.

D's own errand mover pseudocode calls `TierOf(npc.Body)` in `Errands.cs` (D §1.3, line 144), and D29 adds that file to the scan. So the test fails the moment it is written, before any M7 code exists. The substring also collides with C's own planned `StandingLadder.TierOf`.

**Fix.** Give the ladder method a name that is unique to factions, for example `StandingLadder.StandingTierOf`. Ban that name, `.Ladder.`, `StandingLadder`, `StandingOf`, `State.Factions`, `Setup.Factions`, `FactionRules` and `StandingLevel`, but never bare `TierOf`. The stronger form is a reflection check: nothing in the tactical types references a type in `UNNAMED.Domain.Factions`. D §4's G2 (`QuestCode_NeverReadsStanding`) and GH's `PresentationSource_NeverDerivesStanding` also list `TierOf`. They pass today only because neither the quest files nor `src/Presentation` contain the token, so change them the same way.

### F2 (HIGH): NAV006 and N-A1 reject the shipped Ashen Hollow content (A §13.3, §10.4, §10.5 N-X3; kept by D §2.9; E1's STOP rule)

**Claim.** A §13.3 NAV006: "every `NpcSite`, container, station, door approach point and **location centre** shares the spawn's person flood". N-A1 asserts the same, and N-X3 says "the shipped content passes all of them". A's Appendix B.2 item 1 caught this exact class of error in a candidate (NAV007 against Tavar's site) but missed it here.

**Code and content.**
- `content/locations/foldscar.yaml:9` puts the anchor at (153, 0, 48). That is the exact centre of `rock_foldscar_heart`, `circle_m: [153, 48, 1.5]` (`content/regions/ashen_hollow.yaml:108`).
- `content/locations/ruined_cart.yaml:9` puts the anchor at (157, 162). That is inside `cart_wreck`, `box_m: [155.5, 161.2, 158.5, 163.2]` (`ashen_hollow.yaml:85`).

In both cases the node that contains the centre is unwalkable at every class radius, so it cannot share the spawn's flood. The other four anchors are clear: blackvein_cut 25.1 m, den_mouth 5.0 m, herb_patch 3.6 m and outpost 0.6 m from the nearest blocker. The outpost anchor is inside the smithy, which is reachable through its door. E's first slice therefore meets its own STOP condition ("N-A1 finds an authored protected point outside the spawn's flood").

**Fix.** Treat a location as a **Reach** point, the way D §1.2 row 6 already treats nodes and containers: some walkable node in the spawn's flood has its centre within the location's `discovery.radius_m`, or within 1,600 mm of the footprint that contains the anchor. Alternatively, drop location centres from NAV006 and N-A1. Either way, make N-X3's claim true by testing it against the shipped pack.

### F3 (MEDIUM): turning `CameraRig.MaxDistance` into an instance field breaks the Presentation build (B §21.3; D §5.2, the Presentation row)

**Claim.** B §21.3: "`CameraRig.MaxDistance` is a `const` (`CameraRig.cs:23`, used at `:85`); it becomes an instance field". D §5.2 repeats "`CameraRig.MaxDistance` becomes an instance field".

**Code.** `src/Presentation/Perf/PerfRun.cs:56` reads it statically: `new("obstruction", segmentSeconds, CameraRig.MaxDistance, Obstructed)`. An instance field breaks that line.

**Fix.** Adopt GH §H.4.8 (already in E5): keep the `const`, and add `BuildMaxDistance = 9f` and a `Cap` property that `Zoom` clamps to. Amend B §21.3 and D §5.2.

### F4 (MEDIUM): D's freeze plan leaves a compile error (D14; D §1.5 items 1-3)

**Claim.** D14 repoints `SchemaV8ToV9`, `SchemaV12ToV13` and `V12.Player.Companions` to the new `V13.*` shapes.

**Code.** `SchemaV11ToV12` builds `new V12.Player { … Companions = Array.Empty<CompanionDto>() }` (`src/Persistence/Migrations.cs:683-701`). Once `V12.Player.Companions` (`src/Persistence/Sections/SchemaV12.cs:32`) is typed `V13.Companion[]?`, line 701 no longer compiles. F §4.2 lists this fourth repoint; D does not. Separately, D §1.5 item 3 cites `Migrations.cs:741` for `SchemaV12ToV13`'s construction. The `new PlayerDto` is at `:722`; `:741` is the `Posture` line.

**Fix.** Add `Migrations.cs:701` to D14 and §1.5, and cite `:722`.

### F5 (MEDIUM): new `IDialogueFacts` members break a test implementer that C does not list (C §8.1, §15; D §2.8)

**Claim.** "`IDialogueFacts` gains `int StandingLevel(string factionId)`" and `ActDone`, and "`DialogueSystem` and `SpeakerFacts` implement" them (C §8.1, §15).

**Code.** The interface has three implementers, not two:
- `src/World/Runtime/Social.cs:206` (`DialogueSystem`);
- `src/World/Runtime/Social.cs:432` (`SpeakerFacts`);
- **`tests/Domain.Tests/DialogueRulesTests.cs:12`** (`private sealed class Facts : IDialogueFacts`).

The third stops compiling as soon as the members are added.

**Fix.** Add the test fake to C §15 and to E3's change list. Give it explicit implementations; do not use default interface members, which would quietly answer 0.

### F6 (MEDIUM): "load publishes nothing" is cited to a test that cannot see M7 events (B §9.6; D §2.5; F §3.2 row n)

**Claim.** B §9.6: "`Populate` publishes nothing, because a load publishes nothing (`tests/Application.Tests/DeterminismAndViewTests.cs:128-143`)". D §2.5: "Construction and load publish none of them" (the M7 events), with the same citation.

**Code.** `SavingIsNotAnEvent_AndLoadingPublishesNothing` subscribes to only three types: `BodyMoved`, `CellTierChanged` and `LocationDiscovered` (`DeterminismAndViewTests.cs:135-137`). It would stay green if `BuildingSystem.Populate`, `NavigationSystem.Build` or the errand `Populate` published `PiecePlaced`, `StructuresChanged`, `NavigationRebuilt`, `WorkerAssigned`, `NpcArrivedAtWork`, `ActRecorded` or `ReputationChanged`. A's N-W2 covers only `NavigationRebuilt` at construction.

**Fix.** Extend that test, or add `ConstructionAndLoad_PublishNoM7Event`, so that it subscribes to every event type M7 adds. Run it on a new game and on a load of a save that holds pieces, an errand and a non-empty ledger.

### F7 (MEDIUM, resolved by D; B must be amended): B switches `CreatureSystem.Populate` to the piece-bearing space (B §2.4, §21.5)

**Claim.** B §21.5 lists `Creatures.cs:173` among the reads switched to `_context.Space`, and says "The critique confirmed this list is complete".

**Code.** `Populate` samples homes with `Kinematics.IsClear(cx, cz, radius, space, others)` (`src/World/Runtime/Creatures.cs:173-196`). A `CreatureRecord` overrides the body, health and mind but **not** the home (`Creatures.cs:201-220`), and the record has no home field (`src/World/WorldDelta.cs:162-185`). Homes are therefore recomputed on every load. A space that holds pieces makes a home depend on pieces placed after boot, so save-then-continue diverges (A §13.5 B2; D §1.1). The list itself is complete (all 17 `Layout.Space` reads, verified by grep). Switching line 173 is the error.

**Fix.** Amend B §21.5 to exclude `:173`, per D2 and G9.

### F8 (MEDIUM-LOW): B's codec plan does not compile as written; F supersedes it (B §9.2, §9.4; D15)

- **The `Prove` loop.** B §9.2: "`EncodeEntities`' `Prove` loop (`SectionCodec.cs:478-494`) adds each piece's and errand's host cell". But `Prove(string cell, string? hash, EntityId instance)` (`src/Persistence/SectionCodec.cs:481`) takes an `EntityId`, and an errand has none (it is keyed by `NpcId`). F §6.1 changes the third parameter to a string label.
- **The decode tuple.** `DecodeEntitySection` returns a 4-tuple (`SectionCodec.cs:555-596`). `SaveLoader.cs:143-145` deconstructs it, and so do `tests/Persistence.Tests/MigrationTests.cs:148`, `:179` and `:197`. Neither B §9.4 nor D15 lists this hand-written copy; F §6.1 and §6.2 do.
- **D15's count.** D15 says a grep over `src` and `tests` found four construction sites. There is also `tests/World.Tests/WorldDeltaTests.cs:194` (positional; it compiles unchanged) and the `DeltaSnapshot.Empty` initializer (`WorldDelta.cs:190`). Neither needs a change.

**Fix.** Amend B §9.2 and §9.4 to defer to F §6.1 and §6.2.

### F9 (LOW): where kills, and therefore `RecordAct`, happen in the tick (D §1.4 table; C §5.1 K2)

**Claim.** D §1.4: "`_creatures.Tick` | A kill dispatches `RecordAct` inside `Die` … The witness check reads NPC bodies before `_npcs.Tick` moves them". C K2: "`NpcSystem.Tick` runs later in the step".

**Code.** Kills do not happen in `_creatures.Tick`. The player's blows resolve during `_combat.Tick`: `PlayerHits` dispatches `WoundCreature`, then `Die` (`src/World/Runtime/Creatures.cs:643-672`). Damage-over-time kills resolve during `_effects.Tick`: `Harm` → `HarmCreature` → `Die(…, _player)` (`src/World/Runtime/Combat.cs:712-716`; `Creatures.cs:676-682`). `_effects.Tick` runs **after** `_npcs.Tick` (`src/World/Runtime/Simulation.cs:327-329`), so for a bleed kill the witnesses have already turned this tick. This is still deterministic.

**Fix.** Correct the table, and scope K2's ordering note to blow kills.

### F10 (LOW): piece-door rationale cites the wrong code (B §7)

**Claim.** "Flags are declared content and are cell-scoped (`src/World/WorldDelta.cs:250-259`)".

**Code.** `SetFlag` checks only that the ID is a valid `world.*` ID. Declaration is enforced elsewhere: flags are `world_flag` definitions (`content/world_flags/`; `flag_ref` resolves to `world_flag`, `src/Content/ContentChecks.cs:38`), and the load-time definition pass resolves every cell flag (`src/Persistence/SaveLoader.cs:242-252`). The conclusion stands.

**Fix.** Cite those lines instead.

### F11 (LOW): the `ClosedDoors()` call-site count (A §13.5 B2; Appendix B.1)

**Claim.** "the nine `ClosedDoors()` call sites".

**Code.** The list A gives, and a grep, show **eight** call sites: `Systems.cs:83`; `Companions.cs:574`, `:598`; `Creatures.cs:585`, `:618`, `:830`, `:893`; `Combat.cs:570`. The definition is at `Systems.cs:48`.

### F12 (LOW): ware refs in the billet-row note (GH §G.6 E6)

**Claim.** The billet row is appended last "so the ware refs `#00..#02` keep their indices".

**Code.** `Baseline` splits stock by `stack_max` (`src/World/Runtime/Items.cs:494-510`). 60 arrows at `stack_max: 20` (`content/items/ammo/arrow_rough.yaml:9`) give `#00`-`#02`, the vest `#03` and the cap `#04`. The billets become `#05`. The conclusion (append last) is right; the range is `#00..#04`.

### F13 (LOW): "no caller" (C §1 table)

**Claim.** "`SchemaTypeMapper.GetSchemaType` … has no caller".

**Code.** It has a test caller (`tests/Content.Tests/ValidationTests.cs:356-359`). Appendix B's "no caller in `src`" is the accurate form.

### F14 (LOW): "GrantItem never refuses" (B §8.3)

**Code.** It refuses an unknown item ID (`src/World/Runtime/Items.cs:369-370`) before it falls back to the ground (`:371-372`). BLD004 makes this unreachable for piece costs.

**Fix.** Say "never refuses a known item".

### F15 (LOW): `KnownDirectories_Is_Closed_Set` "gains `pieces`" (B §19.1; D §2.9)

**Code.** The test only asserts `Contains` for twelve names (`tests/Content.Tests/ValidationTests.cs:332-350`); it is not a closed-set assertion. The directory list comes from `SchemaResolution`. Adding the line is optional, not required, and the test would not have caught a missing entry.

### F16 (LOW): a planned change written as if it already exists (B §12)

**Claim.** "chest moves (`InventorySystem.Check`, `Items.cs:450-460`, refuses `site.Owner ≠ actor` with 'that chest is not yours')".

**Code.** `Check` has no owner branch (`Items.cs:450-460`), and `ContainerSite` has no `Owner` (`src/Domain/Spatial/RegionLayout.cs:37`). B §3.2 does list `ContainerSite.Owner` as new.

**Fix.** Write it as "gains a refusal".

### F17 (LOW): citation drift, collected

| Draft | Cited | Correct |
|---|---|---|
| A N-A10 | `CreatureTests.cs:526-544` | `:526-542`; the file ends at 543 |
| A §10.5 "must stay green … `SpatialTests`" | a class | `tests/Domain.Tests/Spatial/SpatialTests.cs` is a file holding `TerrainGridTests`, `KinematicsTests` and `TierRulesTests`. There is no `SpatialTests` class, so a `--filter SpatialTests` matches nothing |
| B §1.2 | `iron_billet.yaml:9` (station `forge`) | `:8` (`:9` is `skill_ref`) |
| B §8.2 | fixed nodes at `GameSession.cs:102-105` | `:104-107` |
| D §1.5 item 3 | `Migrations.cs:741` | `:722` (see F4) |
| F §1 | integrity-root trap at `SaveModel.cs:131-132` | `SaveLoader.cs:469` with `SaveModel.cs:31-32`; `:131-132` is `LoadResult.IsComplete` |

---

## 3. Confirmation of the claims the brief singled out

Each item below was re-read in source and is **true as stated in the drafts**, apart from the findings above.

- **How `Kinematics.Step` resolves collision.** Travel is split into sub-steps no longer than r/2 (`Kinematics.cs:144-145`). `Resolve` makes up to four passes, statics then dynamics in list order, so list order matters. The tucked radius applies to static blockers only (`:183-208`, `:192`). Results round to whole mm, half away from zero, and Y is terrain plus lift, so nothing is stood on (`:157-159`). `Blocks` is height-aware (`:166-167`); `IsClear` and `Crosses` are height-blind (`:170-173`; `Blockers.cs:26`). `CanStand` reads only blockers with a clearance (`:176-177`), so pieces with clearance 0 never affect it.
- **Shapes.** Only axis-aligned boxes and circles exist (`Blockers.cs:41`, `:95`). `Separation` treats touching as clear (`:52`, `:102`); `Crosses` treats touching as crossing (`:89`, `:123`).
- **Containers and corpses.** `Take` removes an emptied non-authored container and dispatches `CorpseEmptied` (`Items.cs:558-562`). `RemoveContainer` retires the container and every item ID in it (`WorldDelta.cs:390-400`). `PlaceItem` never touches the registry (`:360-367`). `Materialize` mints the `cnt_` through the registry (`Items.cs:513-523`). `DiscardContainer` calls `RemoveContainer` (`:376-381`). The registry only tombstones (`EntityRegistry.cs:133-142`), and `CreateEntity` throws on an ID it has seen (`:51-52`). B's C1 analysis is exact.
- **`StateDump` and digests.** The dump serialises `CaptureRecord()` and `World.TakeSnapshot()` by reflection (`StateDump.cs:25-46`). The replayable dump masks `^[a-z]{3}_[0-9A-Z]{26}$` (`:93-94`), so F's `container.pce_…` masking fix is needed. `StateDigest` is tag `v1`, tick, player digest, then each cell's `EffectiveCellDigest` (`Simulation.cs:357-364`). The player tag `v9` is at `PlayerState.cs:328`, and the effective-cell tag `v1` at `WorldDelta.cs:587`. No test pins any tag value (grep).
- **How the loader lints.** Validators are chained in `LoadAll` (`ContentLoader.cs:155-197`; Quest at `:195`). Each existing validator reports under one code family (`SOC001`, `CRF001`, …), and WLD codes stop at WLD014. The NAV, BLD and FAC prefixes are unused. `faction` is a registered kind (`SchemaResolution.cs:158-164`). `faction_ref` is a known suffix (`ContentChecks.cs:34`), and an unknown `*_ref` is XREF004 (`:132-135`). `LoadAll_Loads_Yaml_Files` asserts the exact ID list (`ValidationTests.cs:508-547`).
- **`EntityId` methods and `EntityKind` values.**
  - `NewId` uses the wall clock plus crypto random bytes (`EntityId.cs:48-53`). `Create`, `Parse` and `TryParse` exist; there is **no** `Derived`. `Timestamp` decodes the ULID time (`:46`). IDs compare ordinally (`:129-130`), and the Crockford encoding sorts numerically (`:98-110`).
  - `EntityKind` has twelve values with `Character` last; `Building`/`bld` exists and `pce` does not (`EntityKind.cs:12-50`).
  - NPC and creature IDs are `Create(kind, 1, SHA-256[0..10])` (`Social.cs:121-125`; `Creatures.cs:161-166`).
- **The `StateSlice` list.** 18 values; `CellTiers` and `Npcs` are transient (`RuntimeState.cs:19-74`). The owner check sits at `:144-161`.
- **The `Simulation` constructor.** Composition order is `Simulation.cs:116-137`; then `RequireEverySliceOwned`, `_effects.Seed`, `_npcs.Populate`, `_companions.Populate`, `_creatures.Populate` and `_tiers.Settle` (`:139-144`).
- **Tick order.** Movement, tiers, the tier simulations, combat, creatures, companions, NPCs, dialogue, effects, death, discovery, quests, clock (`:320-333`). `Now` is at `:367`.
- **`SystemContext` helpers.** `IsOpen`, `IsSet`, `IsLifted`, `ClosedDoors`, `FindContainer`, `MerchantSites`, `WaresOf`, `TalkReachMm` (1,950), `DistanceToPlayer`, `CorpseSites` and `Obstacles` (`Systems.cs:24-87`). There is no `Space`, `SightWalls` or `Stations` yet.
- **Chest paths in `Items.cs`.**
  - `StackTake` is internal (`:84`).
  - `ExchangeItems` with a null `ItemId` spends only, all or nothing, and checks weight only for a received item (`:327-357`).
  - `Check` refuses `MoveItem`/`TakeAll` on wares, and trading is not reach-checked against the container (`:450-460`).
  - Untouched wares are the authored stock split into `#NN` stacks; a touched container is a record that never re-reads stock (`:486-510`).
- **`TradeSystem.Handle(BuyCommand)`.** `Trader` checks talk reach to the NPC's body (`Social.cs:535-550`); then the ware, the count check (`:491-492`), the price (`:493`) and `Trade` (`:496`). `View` is at `:522-529`. `open_service` only publishes an event (`:373-375`).
- **`Perception.Sees(Body eye, Senses senses, long x, long z, IEnumerable<Blocker> walls)`.** Range, then field of view (`Sin`/`Cos`), then `Crosses`; it returns the distance or null (`Perception.cs:87-106`).
- **`SaveLoader` and `SemanticRebase`.**
  - The definition pass runs only when the content hash differs (`SaveLoader.cs:158-159`). It resolves cell flags and item IDs, but never container keys (`:242-281`), and it rebuilds the snapshot by hand (`:356-358`).
  - `ProveBaselines` covers cells, entities, created instances, containers and creatures (`:361-408`). Transitions are one hop (`:395-396`).
  - `SemanticRebase` carries created instances, containers and creatures "as they are" (`BaselineTransitions.cs:94-117`). `BaselineTransition(Name, From, To, DropVanishedTargets = false)` is at `:22`.
- **`SectionCodec` required-field handling.** Each field added after schema 1 is nullable in the DTO, and decode throws `FormatException("… (required from schema N)")` (`SectionCodec.cs:399-416`, `:570-581`). An unknown enum key throws at decode (`:582-585`); range checks run at apply (`WorldDelta.cs:807-810`). A decode failure in `cells` or `entities` quarantines that section (`SaveLoader.cs:410-430`); a `player` failure is fatal (`:148-155`). Entities carry a per-section `baselines` table (`SectionCodec.cs:202-218`, `:478-494`).

---

## 4. Ledger of verified claims (68)

✓ true as stated · ~ true with a qualification · ✗ false (see §2)

| # | Claim (draft) | Evidence | |
|---|---|---|---|
| 1 | `Kinematics.Step`: sub-steps ≤ r/2; ≤ 4 passes, statics then dynamics; tuck on statics only (A, B, D) | `Kinematics.cs:144-145`, `:183-208`, `:192` | ✓ |
| 2 | Y = terrain + lift; nothing is stood on (00, B §0.3) | `Kinematics.cs:157-159` | ✓ |
| 3 | `Blocks` is height-aware; `IsClear` and `Crosses` are height-blind (A B7, B §0.3) | `Kinematics.cs:166-173`; `Blockers.cs:26` | ✓ |
| 4 | Touching is clear for `Separation` and crossing for `Crosses` (A §3, App. B) | `Blockers.cs:52`, `:102`, `:89`, `:123` | ✓ |
| 5 | Boxes and circles only, axis-aligned (00, A §2) | `Blockers.cs:41`, `:95` | ✓ |
| 6 | `WalkSpace` is one immutable region-wide record (00, D2) | `Kinematics.cs:104` | ✓ |
| 7 | `CanStand` reads only clearance > 0 blockers (D §1.1) | `Kinematics.cs:176-177` | ✓ |
| 8 | `ClosedDoors()` re-reads flags per call: doors, then barriers (A, B, D) | `Systems.cs:48`; `RegionLayout.cs:94-97` | ✓ |
| 9 | `Obstacles()` = closed gates + living creatures + non-companion NPCs (A App. B) | `Systems.cs:82-86` | ✓ |
| 10 | The companion's obstacles include the player and every other NPC (A §8.4, D §1.3) | `Companions.cs:571-581` | ✓ |
| 11 | Creatures pass through non-companion NPCs (A §8.1) | `Creatures.cs:830-836` | ✓ |
| 12 | 17 `Layout.Space` reads in runtime: 13 collision/sight, 4 terrain (A, B §21.5) | grep | ✓ |
| 13 | "Nine" `ClosedDoors()` call sites (A B2) | grep: 8 | ✗ F11 |
| 14 | `Populate` samples homes against `Layout.Space`; a record never overrides the home (A B2, D2) | `Creatures.cs:173-222` | ✓ |
| 15 | `Creatures.cs:173` belongs in the Space switch (B §21.5) | as above | ✗ F7 |
| 16 | A boar's charge into something solid stuns it (A §1, B §13.1) | `Creatures.cs:589-594` | ✓ |
| 17 | 18 slices; `CellTiers` and `Npcs` transient (00, D17) | `RuntimeState.cs:19-74` | ✓ |
| 18 | One owner per slice, checked at composition (00) | `RuntimeState.cs:144-161` | ✓ |
| 19 | Constructor and populate order (A §13.2, D8) | `Simulation.cs:116-144` | ✓ |
| 20 | Tick order; `_npcs` follows `_companions` (D9) | `Simulation.cs:320-333` | ✓ |
| 21 | FIFO drain; `Now` (A §5.1, C §4.4) | `Simulation.cs:268-306`, `:367` | ✓ |
| 22 | One internal command type → one handler; `PlaceNpc` exists (A, D) | `Simulation.cs:369-404`; `Companions.cs:81` | ✓ |
| 23 | `StateDigest` composition and tag (B §9.3, D13) | `Simulation.cs:357-364` | ✓ |
| 24 | `Aim` is the read-only precedent; methods allow-listed, properties exempt (00, A, B §4) | `Simulation.cs:202-206`; `ArchitectureTests.cs:111-131` | ✓ |
| 25 | `EntityId`: `NewId` is wall clock; `Create`/`Parse`/`TryParse`; no `Derived` (00, D4) | `EntityId.cs:48-96` | ✓ |
| 26 | `EntityKind`: 12 values, `Character` last, `bld` exists (B §2.1, §18) | `EntityKind.cs:12-50` | ✓ |
| 27 | NPC and creature IDs are hash-derived (00, A B9) | `Social.cs:121-125`; `Creatures.cs:161-166` | ✓ |
| 28 | Registry throws on a known ID; destroy only tombstones (B C1, D4) | `EntityRegistry.cs:51-52`, `:133-142` | ✓ |
| 29 | Items mint through `NewId` (00, A App. B) | `Items.cs:657`; `EntityRegistry.cs:71-72` | ✓ |
| 30 | `ExchangeItems` with null `ItemId` spends only and mints nothing (B §3.4) | `Items.cs:327-357`, `:406-416` | ✓ |
| 31 | `GrantItem` never refuses (B §8.3) | `Items.cs:367-374` | ~ F14 |
| 32 | The corpse clause removes an emptied non-authored container (B §14.1) | `Items.cs:558-562` | ✓ |
| 33 | `RemoveContainer` retires every item ID; `PlaceItem` does not register (B §10.3) | `WorldDelta.cs:390-400`, `:360-367` | ✓ |
| 34 | `Materialize` mints the `cnt_`; `Baseline` throws on `LootTableId == ""` (B §14.1) | `Items.cs:494-523` | ✓ |
| 35 | Wares move only through `Trade`; no reach check while trading (C §8.2, D12) | `Items.cs:450-460` | ✓ |
| 36 | Stock refs are `#NN` stacks; a touched record never re-reads stock (C §8.2) | `Items.cs:486-510` | ✓ (GH's range ✗ F12) |
| 37 | `TradeSystem.Handle(Buy)` step order (C §8.2) | `Social.cs:484-500`, `:535-550` | ✓ |
| 38 | `open_service` is only an event; unoffered replies are refused and hidden (C §8.1) | `Social.cs:373-375`, `:264-266`, `:306` | ✓ |
| 39 | `BuildNpcs` refuses five named fields and ignores other keys (C §1) | `SocialContent.cs:69-94` | ✓ |
| 40 | `IDialogueFacts` has two implementers (C §8.1) | three, incl. `DialogueRulesTests.cs:12` | ✗ F5 |
| 41 | `Perception.Sees` signature and semantics (C §1, §5) | `Perception.cs:87-106` | ✓ |
| 42 | Sight walls = statics + `ClosedDoors`, copied three times (B §21.4, C §13, D11) | `Creatures.cs:893`; `Combat.cs:569-570`; `Companions.cs:597-598` | ✓ |
| 43 | `RecordDeed` pattern; a kill counts only when `killer == _player` (C §4) | `Creatures.cs:701-706`; `Quests.cs:62` | ✓ |
| 44 | Kills land in `_creatures.Tick`, before `_npcs.Tick` (D §1.4, C K2) | `Creatures.cs:643-682`; `Combat.cs:712-716`; `Simulation.cs:327-329` | ✗ F9 |
| 45 | Switch `Work`: refusal, flag, event (C §4.2) | `Systems.cs:276-292` | ✓ |
| 46 | NPCs turn 18,000 mdeg a tick, skip companions, populate at their site (A §8.4, §13.6) | `Social.cs:107`, `:127-164` | ✓ |
| 47 | Trail and stuck counter persisted; `Order` clears the trail; the oldest mark; 30% headway (A) | `Companions.cs:186-187`, `:308-361`; `PlayerState.cs:52-62` | ✓ |
| 48 | Every `With*` carries only `Posture` (C §14.2, F §6.6) | `PlayerState.cs:222-253` | ✓ |
| 49 | Player digest terms and tag (A §11, C §14.2) | `PlayerState.cs:323-368` | ✓ |
| 50 | `EffectiveCellDigest` term order and tag (B §9.3, F §5.2) | `WorldDelta.cs:582-634` | ✓ |
| 51 | `DeltaSnapshot` shape; hand-built copies (00, D15) | `WorldDelta.cs:188-200`, `:520`; `SaveLoader.cs:146`, `:358`; `BaselineTransitions.cs:112` | ✓ (the tuple at `SaveLoader.cs:143-145`, F8) |
| 52 | Required-from-N decode pattern (A, B, C, F) | `SectionCodec.cs:399-416`, `:570-581` | ✓ |
| 53 | Per-section `baselines`; `Prove` loop (B §9.2, F §6.1) | `SectionCodec.cs:202-218`, `:478-494` | ✓ (signature, F8) |
| 54 | Quarantine on decode failure; a bad player section is fatal (F §11) | `SaveLoader.cs:410-430`, `:148-155` | ✓ |
| 55 | A new section file breaks the integrity root (00, B §9.1) | `SaveLoader.cs:457-470`; `SaveModel.cs:31-32` | ✓ |
| 56 | Schema 13; the migration table; the V12 freeze note (C, D14, F §4) | `SaveModel.cs:20`; `Migrations.cs:69-81`; `SchemaV12.cs:1-33` | ✓ (D omits `:701`, F4) |
| 57 | The definition pass: gated on hash; flags yes, container keys no (F §7) | `SaveLoader.cs:158-159`, `:242-281` | ✓ |
| 58 | `SemanticRebase` carries records as they are; transition record shape (B §8.2, F §6.5) | `BaselineTransitions.cs:22`, `:94-117` | ✓ |
| 59 | Fixed nodes are generator input; the baseline digest covers nodes; the M3f and M6 transitions (B §8.2, F §8.3) | `GameSession.cs:77`, `:104-117`; `Generation.cs:144-162` | ✓ |
| 60 | `StateDump` reflection and the replayable ID mask (B §9.3, F §5.6) | `StateDump.cs:25-46`, `:93-94` | ✓ |
| 61 | Validator chain and lint prefixes (A, B, C, D33) | `ContentLoader.cs:155-197` | ✓ |
| 62 | The `faction` kind, `faction_ref`, and `flag_ref` → `world_flag` (C §1, §4.2) | `SchemaResolution.cs:158-164`; `ContentChecks.cs:34`, `:38` | ✓ |
| 63 | Architecture tests: arrays count as mutable statics; namespaces scanned; the `get_` naming (A, B, D38) | `ArchitectureTests.cs:61-79`, `:85-105`, `:134-155`, `:204-209` | ✓ |
| 64 | No `.Subscribe(` in World/Domain; no physics queries in Presentation; no `Stopwatch` outside `SpikeScene.cs:38` (D G4, G5; IJ) | grep | ✓ |
| 65 | `CameraRig.MaxDistance` is a const used at `:85` only (B §21.3) | `PerfRun.cs:56` also reads it | ✗ F3 |
| 66 | Keys B, T, Y, Z, Delete, PageUp/PageDown, F2 and F6 are free (B, D31, GH) | `Main.cs:1061-1104` | ✓ |
| 67 | Content numbers: speeds, companion config, tier radii and hysteresis, hound sight 30, wolf FOV 140, Kera's site, Tavar at the fold centre, door boxes, pad relief 228/96 mm (recomputed with `HeightAtMm`'s integer rule), deadfall terrace 7.15 m | `base_speeds.yaml:7-11`; `companion.yaml:10-13`; `simulation_tiers.yaml:7-10`; `Tiers.cs:36-47`; `ashen_hollow.yaml:143-155`, `:197` | ✓ |
| 68 | Every location centre is walkable (A NAV006, N-A1) | `foldscar.yaml:9` vs `ashen_hollow.yaml:108`; `ruined_cart.yaml:9` vs `:85` | ✗ F2 |

---

## 5. New APIs: labelled correctly

The following are presented as additions, and a grep confirms that none exists in `src` at `e10d2c4`:
- **Identity:** `EntityId.Derived`, `EntityKind.Piece`.
- **Slices:** `StateSlice.Navigation`, `Structures`, `NpcErrands`, `Factions`.
- **Context and read surface:** `SystemContext.Space`, `SightWalls()`, `Stations()`, `StructureFootprints`, `PersonObstacles`; `Simulation.Space`, `PreviewPlacement`, `Pieces`, `Navigation`, `Factions`, `Acts`, `StructureRevision`.
- **Domain types:** every `Nav*` type; `FactionLedger` and its rows; `PieceRecord`, `NpcErrandRecord`; `BuildAreaSite`; `ContainerSite.InstanceId`/`Owner`; `NpcDefinition.WorksAt`/`FactionId`; `MerchantStock.Requires`.
- **Commands and events:** the five building commands; `OpenDoor`, `RebuildNavigation`, `OperatePieceDoor`, `DamagePiece`, `BeginWork`, `EndWork`, `SpillContainer`, `RecordAct`, `ReportAct`; every new event.
- **Persistence:** `SchemaV13ToV14` and the `V13.*` frozen types.

The only wording slip is F16.

The APIs the drafts treat as existing all do exist:
- **Runtime:** `NpcSystem.InstanceIdOf` (public static, `Social.cs:121`); `Simulation.DynamicBlockers`, `Doors`, `Wares`, `Containers`; `DoorToggled(EntityId Actor, …)`; `PlaceNpc`.
- **Codec and hashing:** `CanonicalHasher.Add(ulong)`, `Add(bool)`, `FinishBytes` (`CanonicalHasher.cs:35-41`).
- **Coordinates and IDs:** `WorldMath.FloorMod` and `FloorDiv`; `CellKey.OfWorld`, `CellKey.AllIn`; `EntityId.Timestamp`.
- **Load and dump:** `SaveToolApp.ReadContent`; `StateDump.Compare`; `GameSession.DisplayName`, `Submit`, `Frame`.
- **Tests:** `Harness`, `Arena.OpenCreatures`; the named tests `ThroughTheLodgeAndRoundIt_…` (C16), `FollowWaitFollow_…`, `LeftFarBehind_…`, `HisState_RoundTrips…`, `SixtyCreatures_TickWithinTheBudget`, `BuyingAndSelling_…`, `EveryStateSlice_HasExactlyOneOwningSystem`, `Commands_CarryNoCameraState_…` (whose banned words `camera`, `view`, `perspective` and `zoom` no M7 command property contains), `SavingIsNotAnEvent_…`, `AScripted200CommandSession_…` (Move and Interact only, as A says).
- **.NET 8 APIs:** `ImmutableCollectionsMarshal` and `System.Int128`.

---

## 6. What the implementer should do with this

1. Before E3, fix F1: rename the ladder method and rewrite the banned-token lists in C §12, D29, D G2 and GH. Otherwise the first faction slice lands red.
2. Before E1, fix F2: redefine the location rule in NAV006 and N-A1. Otherwise the first navigation slice meets its own STOP rule on shipped content.
3. Amend B §21.3, B §21.5, B §9.2, B §9.4 and D §5.2 per F3, F7 and F8. Amend D14 per F4, C §15 per F5, and the load-publishes-nothing guard per F6.
4. The low findings are text corrections. None of them changes a design decision.
