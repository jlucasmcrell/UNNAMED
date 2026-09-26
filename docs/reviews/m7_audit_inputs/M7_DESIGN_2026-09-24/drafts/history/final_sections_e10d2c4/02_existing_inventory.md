## 2. Existing-system inventory (what M7 builds on)

Base: origin/main `e10d2c4`. Every `path:line` is repo-relative and was re-read in the read-only snapshot. The "M7 touch" column is normative: it is the complete list of Phase-1 code M7 changes, and how.

### Decisions

- M7 is specified against the live runtime in `src/World/Runtime`; the M1 scaffold (`ISystem`, `IWorldStateWriter`, `CommandBus`, `TickScheduler`) is not touched.
- `Simulation.Step`'s system order is unchanged; M7 adds no `Tick` (errands move inside `NpcSystem.Tick`).
- `Kinematics.Step`, `MovementRules`, the two `Blocker` shapes and `TerrainGrid` are unchanged; a piece part is an axis-aligned `BoxBlocker` with `ClearanceMm = 0`.
- `Setup.Layout.Space` is never mutated; the piece-bearing space is a separate `SystemContext.Space`.
- `CreatureSystem.Populate` keeps reading `Setup.Layout.Space`.
- The only change to existing item behaviour is `InventorySystem.Put`, which merges into same-kind stacks in (count descending, `ItemId` ordinal) order. Every other Phase-1 touch (for example the any-body close refusal on authored doors) is listed as MINIMAL below.
- NPC and creature ID derivations stay as they are; only pieces and piece chests use the new `EntityId.Derived`.
- `Magic.cs`, `GatheringSystem`, the world generator, `GameSession`'s baseline transitions, `SaveFormat.Current` and `CheckedFiles` are untouched.
- Pre-existing hazards M7 exposes are recorded in `M7_STATUS` and `RISK_REGISTER` (§2.15), not fixed.
- A Phase-1 file not marked MINIMAL or EXTENDED below is not changed. Changing one is a design change and needs a recorded decision.
- Phase-1 tests pass unmodified, except the named edits in §2.18.

### 2.1 Legend and governing sections

| Touch | Meaning |
|---|---|
| NONE | No source line changes |
| MINIMAL | A small, named edit; Phase-1 behaviour is identical unless the row says otherwise |
| EXTENDED | The type or system gains M7 members or branches; existing branches keep their behaviour |
| NEW beside | A new file or type next to the existing one; the existing one is not edited |

Governing sections: §3 Navigation, §4 Building (including the work assignment), §5 Factions, §6 Cross-system contracts, §7 Persistence and migration, §8 UI and presentation, §9 Content.

### 2.2 Simulation core: composition, tick, commands, slices, events

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| Composition root | `src/World/Runtime/Simulation.cs:100-145` | Builds `RuntimeState` and `SystemContext`, registers carried items, constructs 21 systems plus `QuestDebugger` in a fixed list (`:116-137`), `RequireEverySliceOwned` (`:139`), then `_effects.Seed`, `_npcs.Populate`, `_companions.Populate`, `_creatures.Populate`, `_tiers.Settle` | EXTENDED: `_navigation` after `_crafting`, then `_building` (receives it), `_npcs` (claims `Npcs` and `NpcErrands`, receives it), `_relationships`, `_factions` (claims `Factions`; no seed call); `_companions` receives `_navigation`. Rebuild order: `_effects.Seed`, `_building.Populate`, `_navigation.Build`, `_npcs.Populate`, `_companions.Populate`, `_creatures.Populate`, `_tiers.Settle` | §6 |
| Tick | `Simulation.cs:312-339` | Movement, tiers, tier B/C stubs, combat, creatures, companions, NPCs, dialogue, effects, death, discovery, quests, clock (last) | NONE | §6 |
| Command queue | `Simulation.cs:72`, `:268-306`; `Commands.cs:29` | `Queue<GameCommand>`, drained FIFO at tick boundaries; every command logged as `LoggedCommand(Tick, Sequence, Command, RejectedReason)`; a rejection is a string plus `CommandRejected`; 23 command types | EXTENDED: five arms (`PlacePieceCommand`, `DismantlePieceCommand`, `RepairPieceCommand`, `AssignWorkerCommand`, `ReleaseWorkerCommand`) to `_building.Handle(c, WorldTick)` | §4, §6 |
| Internal commands | `Systems.cs:15`; `Simulation.cs:367`, `:369-404` | Synchronous, re-entrant `Dispatch` by type; 32 arms; an unknown type throws; stamped `Now` (stepped tick, or boundary tick while draining) | EXTENDED: nine arms: `RebuildNavigation`→navigation; `OpenDoor`→interaction; `OperatePieceDoor`, `DamagePiece`→building; `BeginWork`, `EndWork`→NPCs; `SpillContainer`→inventory; `RecordAct`, `ReportAct`→factions | §6 |
| State slices | `RuntimeState.cs:19-74`, `:144-161`, `:329-333` | 18 slices; one owner each (boot error otherwise); every write passes `Require(owner, slice)` | EXTENDED: `Navigation` (transient), `Structures`, `NpcErrands`, `Factions`, with gated wrappers; `Npcs`' doc names the errand pose as saved in its errand | §6 |
| Player-held slice seeding | `RuntimeState.cs:97-115` (`Posture = player.Posture`, `:102`) | The constructor copies `PlayerRecord` fields into slices | MINIMAL: also reads `player.Factions` | §5 |
| `SystemContext` | `Systems.cs:24-87` | Shared reads: `IsOpen`, `IsSet`, `IsLifted`, `ClosedDoors` (`:48`), `FindContainer` (`:51-52`), `MerchantSites`, `WaresOf` (`:61-64`), `TalkReachMm` 1,950 (`:67`), `Obstacles` (`:82-86`) | EXTENDED: `Space`, `SightWalls()`, `Stations()`, `StructureFootprints`, `PersonObstacles(npcId)`; `ClosedDoors()` appends closed piece leaves; `FindContainer` appends piece chests; `Obstacles()`' formula unchanged | §6 |
| `SimulationSetup` | `Simulation.cs:14-33` | Immutable rules; init properties default to `XSetup.Empty` | EXTENDED: `Navigation`, `Building`, `Factions` init properties | §6 |
| Read surface | `Simulation.cs:202` (`Aim`), `:255` (`DynamicBlockers`), `:342-351` (`CaptureRecord`), `:357-364` (`StateDigest`, `unnamed.simulation/v1`) | Public methods allow-listed; get-only properties free | EXTENDED: method `PreviewPlacement` (the only allow-list addition); `_previewScratch`; get-only `Navigation`, `Pieces`, `Space`, `Stations`, `StructureRevision`, `StructureAudit`, `WorkAssignments`, `StructureFootprints`, `Factions`, `Acts`; `CaptureRecord` adds `Factions`; digest `v2` hashes `StructureSequence` after the player digest | §6, §7 |
| Events | `Events.cs:10-11`; `src/Application/EventBus.cs:24`, `:50` | Past-tense records for views and tests; published inline; exact-type dispatch in subscription order; no system subscribes; construction and load publish nothing | NEW beside: 15 event types (§6). `DoorToggled` reused: `Actor` may be an NPC instance ID, `DoorKey` a `pce_` value | §6 |
| M1 scaffold | `src/Domain/ISystem.cs`, `IWorldStateWriter.cs`, `ICommandBus.cs`; `src/Application/CommandBus.cs`, `TickScheduler.cs` | Used only by `tests/Domain.Tests/PickUpItem*.cs` | NONE | - |
| Frame loop | `src/Application/GameSession.cs:177-201` | Clamps a frame to 0.25 s, drains, steps whole ticks, drains after each, autosaves every 300 s of playtime with no conversation check | NONE | - |
| Unchanged systems | `Systems.cs` (Clock, WorldFlag, Progression, Discovery), `Items.cs` (Equipment), `Combat.cs` (StatusEffect, Death) | - | NONE | - |

### 2.3 Space: kinematics, blockers, terrain, doors, barriers, tiers

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| `Kinematics.Step` | `src/Domain/Spatial/Kinematics.cs:119`, `:129-160`, `:183-208` | Sub-steps ≤ r/2; up to 4 passes, statics in list order then dynamics; tucked radius for statics only while airborne (`:192`); rounds half away from zero; Y = terrain + lift (`:157-159`) | NONE. Callers pass `_context.Space` | §3, §4 |
| Blockers | `src/Domain/Spatial/Blockers.cs:11`, `:41`, `:95` | `BoxBlocker` and `CircleBlocker`, axis-aligned, integer mm; `Separation` treats touching as clear, `Crosses` as crossing; both height-blind | NONE | §4 |
| `WalkSpace` | `Kinematics.cs:104`; `RegionLayout.cs:63` | One immutable region-wide record, built once from content; cells do not partition movement | NEW beside: `SystemContext.Space = Setup.Layout.Space with { Blockers = authored ++ piece solids in StructureOrder }`, written only by `BuildingSystem` | §4, §6 |
| `IsClear`, `CanStand` | `Kinematics.cs:170-177` | `IsClear` height-blind; `CanStand` reads only blockers with `ClearanceMm > 0` | NONE | - |
| `Layout.Space` reads | 17 in `src/World/Runtime` | 13 collision or sight, 4 terrain-only | MINIMAL: 12 switch to `_context.Space`: `Systems.cs:140`, `:160`, `:216`; `Companions.cs:372`, `:398`, `:567`, `:598`; `Creatures.cs:588`, `:623`, `:837`, `:893`; `Combat.cs:570`. `Creatures.cs:173` stays (with a one-line comment). Terrain-only `Companions.cs:132`, `:519`, `Creatures.cs:762`, `Social.cs:136` stay | §4, §6 |
| Terrain | `src/Domain/Spatial/TerrainGrid.cs:51`; `content/regions/ashen_hollow.yaml:15-62` | Integer `HeightAtMm`; authored 41 × 41 grid at 5 m; outside every baseline hash | NONE (pads drape; no flattening) | §4 |
| Region sites | `src/Domain/Spatial/RegionLayout.cs:12-49`, `:60-98` | `DoorSite`, `SwitchSite`, `BarrierSite`, `LocationSite`, `ContainerSite(Key, LootTableId, XMm, ZMm, StackSlots)`, `StationSite(Key, Kind, XMm, ZMm)`, `NpcSite` | MINIMAL: `BuildAreaSite`, `RegionLayout.BuildAreas`; `ContainerSite.InstanceId` and `.Owner` (init, null for authored sites) | §4, §9 |
| Door and barrier state | `Systems.cs:48`; `RegionLayout.cs:94-97`; `Simulation.cs:169-183` | `ClosedDoors()` re-reads cell flags on every call: authored doors, then standing barriers; a flag lives in the cell of its footprint centre | EXTENDED: closed piece leaves appended in `StructureOrder`; piece doors keep `door_open` on their row, never a flag | §4 |
| `InteractionSystem` | `Systems.cs:240-293` | Player-only; door reach 1.6 m from the body; closing refused only when the player overlaps (`:268`); a switch sets its flag once (`:276-292`) | EXTENDED: a `pce_` target dispatches `OperatePieceDoor`; `Handle(OpenDoor)` for companions and errand NPCs (open only); the close refusal checks the player, every NPC body (companions included) and every living creature, for authored and piece doors; `RecordAct("switch_set", flag)` after an accepted `SetWorldFlag` (`:288`) | §3, §4, §5 |
| Tiers | `src/Domain/Spatial/Tiers.cs:20-47`; `Systems.cs:471`, `:521` | A cell enters A within 140 m and stays A to 160 m; not saved; `Settle` recomputes on load; B and C are stubs | NONE. The errand mover is not tier-gated | §3 |
| Perception | `src/Domain/Creatures/Perception.cs:87-107` | `Sees`: range, `Sin`/`Cos` field of view, `Crosses` | NONE (factions do not witness in M7) | §5 |

### 2.4 Companions and NPCs

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| Companion follow | `src/World/Runtime/Companions.cs:325-361` | Catch-up at > 30 m or 80 ticks without headway; else the player in clear view, else the newest trail mark in clear view, else the oldest; headway = displacement ≥ 30% of expected travel | EXTENDED: `CompanionState.Route`; Route and Nav branches; `OpenDoor` dispatch; route resets on order, downed, up, catch-up and fall; a refused `OpenDoor` adds 1 to `StuckTicks` | §3 |
| Trail | `Companions.cs:308-316`, `:187` | A mark per metre, 48 kept; an order clears the trail | NONE | - |
| Catch-up and ring | `Companions.cs:367-408` | Teleport near the player; the safety net | MINIMAL: the space switch at `:372`, `:398` | §3 |
| Companion obstacles | `Companions.cs:571-581` | Closed doors, living creatures, the player, every other NPC | MINIMAL: moved to `SystemContext.PersonObstacles(npcId)`, shared with errands; same set | §3, §6 |
| Clear view | `Companions.cs:584-598` | Three `Crosses` segments against statics and closed doors | MINIMAL: `Walled` reads `SightWalls()` | §4 |
| Conversation hold | `Companions.cs:286-290` | Stands facing the player while talking; reads the transient conversation | NONE (recorded hazard, §2.15) | - |
| Recruit | `Companions.cs:160-171` | Idempotent; no relationship requirement | MINIMAL: refuses an NPC that has an errand | §4 |
| Copy sites | `Companions.cs:125-143` (`Populate`), `:145` (`Records`) | Carry every saved companion field | MINIMAL: both carry `Route` | §7 |
| `CompanionRecord` | `src/World/PlayerState.cs:52-62` | Saved since schema 12 (trail, stuck ticks) | EXTENDED: `Route` init property | §3, §7 |
| `NpcSystem` | `src/World/Runtime/Social.cs:105-165` | `Populate` places every `NpcSite`; bodies transient (`NpcState`, `:88`); `Tick` turns non-companions 18,000 mdeg per tick towards the talker, else back to site facing; `PlaceNpc` moves companions (`:141-147`) | EXTENDED: `partial`; claims `NpcErrands`; the errand mover in `Errands.cs` runs first in `Tick`, NpcId order; the facing loop skips errand NPCs; `Populate` places errand NPCs at their pose and drops an errand whose NPC is a companion; `BeginWork`, `EndWork` | §3, §4 |
| NPC identity | `Social.cs:121-125` | `EntityId.Create(Npc, 1, SHA-256("unnamed.npc/v1", npcId)[0..10])` | NONE | - |
| `NpcDefinition` | `src/Domain/Social/Social.cs:10-16` | Id, name, role, services, merchant, dialogue; no faction | EXTENDED: `FactionId`, `WorksAt` (init) | §4, §5 |

### 2.5 Creatures

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| Spawn homes | `src/World/Runtime/Creatures.cs:171-225` | Up to 16 samples on channel `spawn`, first `IsClear` against `Setup.Layout.Space` and earlier homes; a record overrides the body, never the home; homes recomputed on every load | NONE | §6 |
| Steering | `Creatures.cs:820-838`, `:869` | Straight `MoveIntent` plus `Kinematics.Step`; arrived within 700 mm; no path search | MINIMAL: `Step` reads `_context.Space` (`:588`, `:623`, `:837`); no pathing | §4 |
| Charge and lunge | `Creatures.cs:565-624` | Fixed-line charge; blocked by something solid → `CreatureStunned` | NONE beyond the space switch; charges stay un-pathed | - |
| Sight walls | `Creatures.cs:893` | `Walls()` = statics + `ClosedDoors()` | MINIMAL: delegates to `SightWalls()` | §4 |
| Death | `Creatures.cs:701-714` | `RecordDeed(Killed)` only when the killer is the player | MINIMAL: `RecordAct("creature_killed", defId, x, z)` right after `RecordDeed`; the block gains braces | §5 |
| Tier gate | `Creatures.cs:264`, `:897` | Acts only in tier-A cells | NONE (recorded hazard) | - |
| Identity and record | `Creatures.cs:162-166`; `src/World/WorldDelta.cs:162-185` | Hash-derived ID; `CreatureRecord` has no home field | NONE | - |

### 2.6 Combat traces

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| Melee sweep | `src/World/Runtime/Combat.cs:495-507` | Each living creature in reach and arc and not walled is struck; nothing struck publishes `AttackMissed` | EXTENDED: when nothing is struck at the active window's last tick, `FirstStop(body, reach)` may yield a piece part: dispatch `DamagePiece(pieceId, 10, "melee")` in place of that swing's `AttackMissed` | §4 |
| `Trace` and `Loose` | `Combat.cs:511-545` | First creature on the facing line, else `Sin`/`Cos` bisection to 10 mm against walls; feeds shots and `Simulation.Aim` | MINIMAL: walls read through `SightWalls()`; `FirstStop` sits beside it and reuses the same bisection with identical outputs; `Loose`'s signature is unchanged | §4 |
| `Walled` | `Combat.cs:569-570` | Statics + `ClosedDoors()` | MINIMAL: `SightWalls()` | §4 |
| Formulas | `src/World/Runtime/Magic.cs:114` | `Loose(formula.Id, …)` | NONE; bolts and arrows stop at piece walls and damage nothing | §4 |
| Targets | `Combat.cs:496`, `:554` | Only creatures; NPCs have no health | NONE | §5 |
| `PlayerCombat` | `RuntimeState.cs:128` | Transient; a load starts at rest | NONE. Building commands read only `Defeated`; no busy clause | §4 |

### 2.7 Items, containers, corpses, created instances

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| `ExchangeItems` | `src/World/Runtime/Items.cs:91`, `:327-357` | Spends listed stacks all-or-nothing; a null `ItemId` mints nothing | NONE; placement and repair spend through it | §4 |
| `GrantItem` | `Items.cs:106`, `:367-374` | Refuses an unknown item; else `Put`, overflow to the ground | NONE; dismantle refunds through it | §4 |
| `Put` merge | `Items.cs:569-609`, `MergeInto` `:611-627` | Fills same-kind, same-quality stacks in `ItemId` ordinal order (`:576`, `:600`) | MINIMAL, behaviour change: order (count descending, `ItemId` ordinal) at `:576` and `:600`; identical when one partial stack exists | §4, §6 |
| Minting | `Items.cs:657` | `NewItem` → registry inferring overload → wall-clock ULID | NONE; M7 systems mint nothing themselves | §6 |
| `Check` | `Items.cs:450-460` | Refuses `MoveItem`/`TakeAll` on wares; no owner concept | EXTENDED: refuses a non-owner on a piece chest | §4 |
| `Baseline` | `Items.cs:494-510` | Loot-table roll (channel `loot`), or authored stock split by `stack_max` into `{key}#NN` refs | MINIMAL: empty when `LootTableId == ""` | §4 |
| `Materialize` | `Items.cs:513-523` | Mints a `cnt_` on first change | MINIMAL: uses `site.InstanceId` when present | §4 |
| Corpse clause | `Items.cs:553-562` | An emptied non-authored container is removed and `CorpseEmptied` dispatched | MINIMAL: condition gains `&& site.InstanceId is null` | §4 |
| `DiscardContainer` | `Items.cs:377-381`; `WorldDelta.cs:390-400` | Removes a container and retires every ID in it | NONE; dismantle uses it | §4 |
| Spill | - | - | NEW beside: `Handle(SpillContainer)`; `WorldDelta.ReleaseContainer` retires only the `cnt_`; items land via `PlaceItem` (`WorldDelta.cs:360-367`) with their IDs | §4 |
| Records | `WorldDelta.cs:54-61`, `:76`, `:347-354` | `CreatedEntityRecord` (cm in host cell); `ContainerRecord` (schema 6); `PlaceCreated` unused | NONE; piece chests are ordinary `ContainerRecord`s | §4, §7 |
| Trade moves | `Items.cs:97`, `:456-459` | Only `Trade` moves wares | NONE | - |

### 2.8 Gathering and crafting stations

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| `GatheringSystem` | `src/World/Runtime/Crafting.cs:58-135` | Nodes on channel `gather`; fixed nodes are generator input | NONE (timber comes from an authored container) | §9 |
| Station check | `Crafting.cs:168-170` | `Layout.Stations.Any(kind && within reach)` | MINIMAL: `_context.Stations()` (authored, then piece stations in `StructureOrder`) | §4 |
| Craft refusals and spend | `Crafting.cs:158-161`, `:178`, `:192` | Dead and busy checks; spends quality descending then `ItemId`; channel `craft` | NONE | - |
| Recipes | `content/recipes/smithing/march_spear.yaml:8` | Station kind `anvil` | NONE; the bench piece is an `anvil` | §4, §9 |
| Fixed nodes and transitions | `src/Application/GameSession.cs:105-117` | Two registered transitions (M3f, M6) | NONE | §7 |

### 2.9 Dialogue, relationships, trade

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| `RelationshipSystem` | `Social.cs:177-198`; `src/Domain/Social/Social.cs:30-40` | Five dimensions in [-100, 100]; moved only by dialogue and quest rewards | NONE | §5 |
| `DialogueSystem` | `Social.cs:206-422`; `TalkCommand` `:225-246` | Closed condition and consequence sets; the open conversation is transient (`:90-91`); flags read in the speaker's cell | EXTENDED: `report_act` → `ReportAct`; facts `StandingLevel`, `ActDone`; `TalkCommand` refuses an errand NPC whose persisted phase is `to_work` or `to_home` | §4, §5 |
| `SpeakerFacts` | `Social.cs:432-459` | Debugger view of the same facts | EXTENDED: the two facts | §5 |
| `IDialogueFacts` | `src/Domain/Social/Social.cs:125-159` | Eight facts, `Holds` is a closed switch; three implementers, one in `tests/Domain.Tests/DialogueRulesTests.cs:12` | EXTENDED: `StandingLevel`, `ActDone`, two `Holds` arms; every implementer gains explicit members | §5 |
| `TradeSystem` | `Social.cs:468-550` | No state; reach to the NPC's body; `BuyCommand` needs no conversation; `open_service` only publishes | EXTENDED: `Withheld` in `Buy` and `View`; reach, wares location (`WaresOf`, `Systems.cs:61-64`) and pricing unchanged | §5 |
| `MerchantStock` | `src/Domain/Items/Items.cs:220` | `(ItemId, Count, PriceBias)` | EXTENDED: `Requires` init property | §5 |

### 2.10 Quests and world flags

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| `QuestSystem` | `src/World/Runtime/Quests.cs:70` (class), `:101-116` (`Handle(RecordDeed)`), `:118-137` (`Tick`) | Runs after every other system except the clock; deed fan-in `RecordDeed` (`:61`) | NONE | - |
| Not-built vocabulary | `src/Domain/Quests/Quests.cs:141-160`, `:205-206` | `construct_building` "building (M7)"; `faction_reputation`, `faction_state` "factions (M7)"; reward `reputation` | MINIMAL (text): the faction objectives' reason becomes "faction quest content (M9)"; all stay not built | §5 |
| `QuestDebugger` | `src/World/Runtime/QuestDebugger.cs:372` | `DescribeCondition` switch with a `ToString()` fallback (`:386`) | EXTENDED: `reputation`, `act_done` arms, pinned by `DescribeCondition_NamesStandingAndActDone` (E3, §2.18), because a missing arm falls back silently | §5 |
| `QuestDebugger.Recipe` | `src/World/Runtime/QuestDebugger.cs:392` | Names the nearest station from `Setup.Layout.Stations` (authored only) | MINIMAL: reads `_context.Stations()`, so the F4 debugger also names piece stations | §4 |
| World flags | `Systems.cs:296-316`; `WorldDelta.cs:250-262` | `world.*` values per 100 m cell; 0 is absence | NONE; neither piece doors nor factions use flags | §4, §5 |

### 2.11 Identity

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| `EntityId` | `src/Domain/EntityId.cs:46`, `:48-53`, `:58`, `:129` | `NewId`: wall-clock ms + 80 random bits; `Create` from explicit parts; ordinal comparison | EXTENDED: `Derived(kind, ordinal, tag, salt)` beside `Create`; `Create` and `Timestamp` doc comments name derived identities | §4 |
| `EntityKind` | `src/Domain/EntityKind.cs:12-50`, `:71-84` | 12 kinds, `bld` present; inference for item, creature, npc, quest only | MINIMAL: append `Piece` → `pce` | §4 |
| `EntityRegistry` | `src/EntityRegistry/EntityRegistry.cs:44-66`, `:71-85`, `:133-142` | Explicit ID throws if seen; `NewId` overload; destroy only tombstones; never saved | NONE | - |
| Hashing and RNG | `src/Domain/CanonicalHasher.cs`; `src/World/StableRandom.cs` | SHA-256 canonical hasher; counter-based channels | NONE; M7 opens no random channel | - |

### 2.12 Persistence (schema 13)

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| Format | `src/Persistence/SaveModel.cs:12-33` | Container 1; schema 13; `manifest.json`, `player.msgpack`, `cells.msgpack`, `entities.msgpack` + `sections.sha256` | MINIMAL: `SchemaVersion` 14 | §7 |
| Migrations | `src/Persistence/Migrations.cs:69-81` | 12 ordered steps | EXTENDED: `SchemaV13ToV14` appended; repoints `:566` (V13.EntitiesSection), `:701` (V13.Companion), `:722` (V13.Player), `Sections/SchemaV12.cs:32` | §7 |
| Frozen shapes | `src/Persistence/Sections/SchemaV1..V12` (no V7, no V13) | Each step reads frozen N, writes N+1 | NEW beside: `Sections/SchemaV13.cs` | §7 |
| Codec | `src/Persistence/SectionCodec.cs:21`, `:75`, `:203`, `:409`, `:481`, `:556` | String-keyed MessagePack; later fields nullable, required on decode; `Prove(cell, hash, EntityId)`; `DecodeEntitySection` returns a 4-tuple | EXTENDED: `factions`, companion `route`, `pieces`, `structure_seq`, `npc_errands`; `Prove`'s third parameter becomes a string label; `DecodeEntitySection` returns a `DeltaSnapshot` | §7 |
| Loader | `src/Persistence/SaveLoader.cs:142-155`, `:158-159`, `:206-359`, `:361-408`, `:410-430` | Decode, definition pass only on a content-hash change, baseline proof, quarantine; hand-built snapshots at `:146`, `:358` | EXTENDED: the pass covers piece `def_id`, errand `npc_id`, standing, knowledge and act IDs; a standing merge to 0 drops the row with a Warning; an `ArgumentException` from the pass maps to a Blocker; `with` rebuilds; proof covers piece and errand host cells; `ResolveDefinitions` (`:206`) goes from private to internal and `Persistence.csproj` gains `<InternalsVisibleTo Include="Persistence.Tests" />`, so G11 can call it and `SemanticRebase.Apply` (E2 commit 3) | §7 |
| Rebase | `src/Persistence/BaselineTransitions.cs:22`, `:94-117` | Created, containers, creatures carried as they are | MINIMAL: `with` copy carrying pieces and errands; no new transition | §7 |
| `WorldDelta` | `src/World/WorldDelta.cs:188-200`, `:453-521`, `:531-576`, `:582-635` | `DeltaSnapshot`; `TakeSnapshot` is the diff authority; `FromSnapshot` validates each record; `EffectiveCellDigest` v1 (`:587`) | EXTENDED: `PieceRecord`, `NpcErrandRecord`, `StructureSequence`; stores, readers, internal mutators, `TryApply*`, the chest clause; effective-cell v2 | §7 |
| `PlayerRecord` | `src/World/PlayerState.cs:92-369`, `:222-253`, `:328` | Seven `With*` copies hand-list `{ Posture = Posture }`; digest `unnamed.player/v9` | EXTENDED: `Factions`; every `With*` carries it; digest v10 | §7 |
| `StateDump` | `src/Application/StateDump.cs:25-46`, `:93-94` | Reflection over `CaptureRecord()` and `TakeSnapshot()`; replayable mode masks `^[a-z]{3}_[0-9A-Z]{26}$` | MINIMAL: a second mask for `container.pce_…` keys | §7 |
| Fixtures | `tests/Persistence.Tests/Fixtures/v1..v13`, `content-0.1.0..0.1.6`, `content` 0.2.8 (`HistoricalFixtureTests.cs:16`); `CanonicalState.cs`; `tests/M2.Probe` | One fixture per schema, never edited; `expected.json` via `UNNAMED_WRITE_FIXTURE_EXPECTATIONS=1` | EXTENDED: `v14`, `content-0.1.7`, current pack 0.2.9 (0.2.10 on a later lint failure); older `expected.json` gain only empty M7 fields | §7 |
| Save store | `src/Persistence/SaveStore.cs:41-45`, `:474` | 300 s autosave; command log never written | NONE | - |

### 2.13 Content kinds and lints

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| Validator chain | `src/Content/ContentLoader.cs:155-197` | Progression, ContentChecks, Item, World, Combat, Magic, Crafting, Social, Quest; semantic lint runs only if every file loaded | EXTENDED: Navigation, Building, Faction after Quest | §9 |
| Kinds | `src/Content/SchemaResolution.cs:57-297` | 28 kinds; `faction` registered (`:158-164`) with no content; no piece kind | MINIMAL: register `piece` (directory `pieces`) | §9 |
| Reference suffixes | `src/Content/ContentChecks.cs:18-44` | `faction_ref` resolves (`:34`); unknown `*_ref` is XREF004 | MINIMAL: `piece_ref` | §9 |
| Social content | `src/Content/SocialContent.cs:26-30`, `:69-94` | Closed condition and consequence lists; five NPC fields refused, others ignored | EXTENDED: NPC `faction_ref`, `works_at`; `reputation`, `act_done`; `report_act`; the `add_reputation` refusal text | §5, §9 |
| Item and world content | `src/Content/ItemContent.cs:191-204`; `src/Content/WorldContent.cs` | Stock rows; WLD001-WLD014 | MINIMAL: stock `requires`; region `build_areas` and WLD015 | §9 |
| Config groups | `content/config/*.yaml` (13) | One group per file | NEW beside: `config.navigation`, `config.building`, `config.factions` | §9 |
| Region | `content/regions/ashen_hollow.yaml:146-163` | Three containers, four NPC sites, two stations | EXTENDED (data): `container.timber_stack` at (84, 118) with 80 timber, `build_area.hollow_crossing`; no generator input changes | §9 |
| Boot wiring | `GameSession.cs:79-119` | Builds every setup; registers two transitions | EXTENDED: builds `NavConfig`, `BuildingSetup`, `FactionSetup` | §6 |
| ID list test | `tests/Content.Tests/ValidationTests.cs:508-547` | Asserts the exact sorted ID list | MINIMAL: gains every new ID | §9 |

### 2.14 Presentation

| Element | Where | Today | M7 touch | § |
|---|---|---|---|---|
| Input map | `src/Presentation/Main.cs:1061-1104` | Actions in code, `PhysicalKeycode`; B, T, Y, Z, Delete, PageUp, PageDown, F2, F6 free | EXTENDED: 17 named actions | §8 |
| Input gating | `Main.cs:374-407`, `:489` | Dialogue takes 1-9; combat needs a captured mouse and no panel | EXTENDED: build branch; combat needs `captured && !build`; Esc leaves build mode first | §8 |
| Refusal toasts | `Main.cs:662`, `:780`, `:864` | Toasts only listed command types | MINIMAL: the five building commands join a filter | §8 |
| Other `Main` hooks | `Main.cs:778`, `:916`, `:1005`, `:1021-1031`, `:1041-1047` | Relationship log line; `Resync`; `OpenStation` reads `Layout.Stations`; `Describe`; `ParseArguments` value list | MINIMAL: reported `ReputationChanged` log line; `StructuresView.Sync` and build exit in `Resync`; `simulation.Stations`; `pce_` keys named via `DefId`; `--build-shots` | §8 |
| Prediction and focus | `src/Presentation/Player/PlayerController.cs:127`, `:186`, `:202` | `Kinematics.Step` against `setup.Layout.Space` + `DynamicBlockers`; focus from layout doors and stations | MINIMAL: `simulation.Space`; piece doors; `simulation.Stations`; submitters `Place`, `Dismantle`, `Repair`, `Assign`, `Release` | §8 |
| Camera | `Player/CameraRig.cs:23`, `:85`; `Perf/PerfRun.cs:56` | `const MaxDistance = 6f`, read statically by `PerfRun` | MINIMAL: `const BuildMaxDistance = 9f`; instance `Cap`; `Zoom` clamps to `Cap` | §8 |
| Structures | `Greybox/HollowView.cs:36`, `:205`, `:374-381` | Built once at boot; camera colliders on layer 1; roofs by ID prefix | NONE. NEW beside: `StructuresView`, `NavigationOverlay`, `BuildMode` | §8 |
| Other views | `Greybox/ItemsView.cs`, `CreaturesView.cs`, `NpcsView.cs`, `CraftingView.cs`; `Art/**`; `Audio/**` | NPCs followed, not predicted | NONE | - |
| HUD, help, palette | `Ui/Hud.cs`; `Ui/HelpPanel.cs:14-45`, `:48`; `Greybox/Palette.cs` | Fixed HUD slots; F1 sections are a hard-coded table | MINIMAL: `Hud.SetBuild`; a BUILDING section and two DEVELOPER rows, `LeftSections` 4; five additive materials | §8 |
| Debug panels | F3 overlay (`Main.cs:476`); F4 quest debugger (`Ui/JournalPanel.cs`) | Read-only `CanvasLayer` panels | NONE. NEW beside: F2 structures and navigation, F6 factions | §8 |
| Scripted modes | `Smoke.cs`; `Playthrough.cs:38`, `:71`; `DeltaShots.cs:43`; `UiShots.cs` | `--playthrough` and `--delta-shots`: fixed seed (`Main.cs:103`), one tick per frame (`:305`), transcripts and `StateDump` JSON; `--smoke`: random seed, one tick per frame; `--ui-shots`: random seed, real-time frames | EXTENDED: faction beats appended to `--playthrough`; NEW beside: `BuildShots.cs`. Smoke, delta and UI shots NONE | §8 |
| Performance | `Perf/FrameStats.cs:78`; `Perf/PerfRun.cs` | Nine CSV columns; no simulation timing | MINIMAL: `sim_ms`, `ticks`, `save_ms` appended; `--perf` unchanged | §8 |
| Godot navigation | `Spike/SpikeScene.cs:159-160` | The only use, measurement only | NONE; banned elsewhere in `src/Presentation` | §3 |

### 2.15 Phase-1 facts that surprise newcomers

1. Events are presentation-only. No system subscribes; cross-system work is a synchronous internal command (`Simulation.cs:369-404`).
2. Commands apply FIFO only at tick boundaries. A command submitted in a frame applies at that frame's first drain, and takes physical effect in the next `Step`.
3. The clock advances last, so everything in step N+1 reads `WorldTick == N` and stamps `N+1`.
4. Movement ignores cell seams: one `WalkSpace` for the region. `den_rock_west` already straddles x = 100 (`ashen_hollow.yaml:94`).
5. Nothing is stood on. Y is terrain height plus jump lift; there is no slope limit or step height.
6. Non-companion NPC bodies are not saved, and NPCs never translate today. Their facing history is transient.
7. The open conversation is transient, and autosave does not check for one.
8. The M1 scaffold is test-only; `ARCHITECTURE.md` §4 still describes it as the system model.
9. `faction` is already a registered kind, and `faction_ref` already resolves. The NPC builder silently ignores unknown keys.
10. Item IDs are wall-clock ULIDs, unordered within a millisecond. The raw `StateDigest` hashes them, so fresh runs compare through the replayable dump.
11. NPC and creature IDs are hash-derived through `EntityId.Create`, despite the `EntityId` doc comment (`EntityId.cs:18`).
12. World flags are cell-scoped. Dialogue reads them in the speaker's current cell, quests in a location's cell.
13. Creature homes are recomputed on every load from `Setup.Layout.Space`; no record stores a home.
14. Player blows kill during `_combat.Tick`, bleeds during `_effects.Tick`, which runs after `_npcs.Tick`.
15. `BuyCommand` needs no conversation: `open_service` only opens a panel.
16. The player cannot harm an NPC, and NPCs have no health.
17. The authored layout (structures, doors, containers, stations, sites) is outside every `baseline_hash`. Fixed nodes are generator input.
18. `Math.Sin`, `Cos` and `Atan2` sit in authoritative paths (perception, traces, facing); determinism is proven on one machine only.
19. `StateDump`'s replayable mode sorts arrays by content, so in replayable comparisons trail and route order are checked only by the raw dump and the digests.
20. `Simulation`'s public methods are allow-listed; get-only properties are free. The presentation scan bans the literal `.Step()`, so no presentation helper may be named `Step`.
21. Four sites hand-build a `DeltaSnapshot` (`WorldDelta.cs:520`, `SaveLoader.cs:146`, `:358`, `BaselineTransitions.cs:112`), and seven `With*` methods hand-list `PlayerRecord` init properties.
22. A load-phase content error hides every semantic lint, and `DIR003` does not fail the lint CLI.

### 2.16 Pre-existing hazards M7 records, not fixes

Each goes into `M7_STATUS`, and the first two also into `RISK_REGISTER`.

| Hazard | Where | Why it matters | Owed |
|---|---|---|---|
| Tier hysteresis is unsaved yet gates movers | `Companions.cs:263`; `Creatures.cs:264`; `Tiers.cs:36-47` | A cell 140-160 m away is A in a continuing world and B after a load | Before M9: save tiers, or use a hysteresis-free gate |
| Companion conversation hold reads the transient conversation | `Companions.cs:286-290` | A save during a conversation can make continue differ from load | Unscheduled |
| `PlayerCombat` is transient | `RuntimeState.cs:128`; `Crafting.cs:158-161` | Crafting's busy check can differ across a save | Unscheduled; building drops the busy clause |
| ID-ordered spends | `Items.cs:387` (`ConsumeItem`); `Crafting.cs:178` | Same-millisecond mints can change which stack is spent | Unscheduled; `Put` and placement check 14 are fixed in M7 |
| Float paths into persisted state | `Perception.cs:95-97`; `Combat.cs:527-542`; `src/Domain/Combat/Combat.cs:238-243` | `FirstStop` decides which piece loses health; `FacingTowards` sets saved errand facing | Evidence replays run on one OS |
| Layout outside the baseline hash | `src/World/Generation.cs:144-162` | Pieces over a later layout edit are audited, not proven | Unscheduled |
| Command log never saved | `SaveStore.cs:474` | PERSISTENCE T-28 is unimplemented | Not M7 |

### 2.17 Not in M7

| Item that drafts touched | Belongs to |
|---|---|
| Faction witnessing through `Perception.Sees` (and an integer witness cone) | M9 |
| Piece damage from shots and formulas (`Loose` parameter, `Magic.cs:114` hook) | A later milestone, with the other reserved damage sources |
| A renewable timber node, its baseline transition and a frozen M6 fingerprint | A later milestone that wants renewable timber |
| Save deferral or a persisted conversation | Unscheduled (recorded hazard) |
| Persisted tiers or a hysteresis-free gate | Before M9 |
| Creature pathing (return home, leash) | A later milestone; declined in M7 |
| `--perf-world`, its generator, `M7_COST_TABLE.md`, building and faction counters, the other `FrameStats` columns, the 30-minute companion soak | Optional in M7 (§14 switch-on conditions); the soak is owed M6 RK-05 work |
| Attackable NPCs, attack legality, crime, bounty, pardon, territory gating | Beyond M7 (owner question 2) |
| `faction_reputation`/`faction_state` objectives, a player-facing faction screen | M9 |
| Command-log persistence (T-28) | Not scheduled |

### 2.18 Tests owned by this section

The only permitted Phase-1 test edits are those §12.10 lists by name ("Edited by name"); anything else is STOP S1.

| Test | Project | What it proves |
|---|---|---|
| `Put_MergesIntoTheFullestStackFirst_WhateverTheItemIds` (new) | Application.Tests (`ItemTests.cs`) | Two carried partial stacks {15, 10} of one kind and quality, in either `ItemId` order, receive 7 and end as {20, 12}; with one partial stack the result equals Phase 1 |
| `SplittingAndMerging_WithinTheInventory` | Application.Tests | The `Put` change keeps Phase-1 split and merge behaviour, unmodified |
| `DescribeCondition_NamesStandingAndActDone` (new, E3) | Application.Tests (`QuestTests.cs`) | F4's `DescribeCondition` gives §5.7.1's wording for a `reputation` and an `act_done` condition, never the `ToString()` fallback |
| `SixtyCreatures_TickWithinTheBudget` | Application.Tests | The creature tick stays under 4 ms, unmodified |
| `ThroughTheLodgeAndRoundIt_NoSnagHoldsHimFifteenSeconds` and every other `CompanionTests` test (`HisState_RoundTripsThroughASave_FieldByField_AndGoesOnTheSame` gains only its route assertion, E4) | Application.Tests | Companion behaviour is unchanged where the trail suffices |
| `ADodgedCharge_RunsTheBoarIntoTheRock_AndStunsIt` | Application.Tests | Charges stay un-pathed and still stun on solid ground |
| `AScripted200CommandSession_ThroughFrames_EqualsItsReplayThroughTheCommandBus` | Application.Tests | Replay still equals the run, unmodified |
| `SavingIsNotAnEvent_AndLoadingPublishesNothing` | Application.Tests | Save and load publish nothing, unmodified |
| `EveryStateSlice_HasExactlyOneOwningSystem` | Application.Tests | The four new slices each have one owner, unmodified |
| `Commands_CarryNoCameraState_SoEveryPerspectiveSubmitsTheSameThing` | Application.Tests | No M7 command carries camera state, unmodified |
| `TheSameScript_PlaysTheSameGame_WhateverTheFreshIdsAre` | Application.Tests | Fresh-run equality through the replayable dump, unmodified |
| `BuyingAndSelling_MoveCoinAndGoodsExactly_AndRefuseCleanly` | Application.Tests (`NpcTests.cs:288`) | Kera's existing wares still trade at neutral standing, unmodified |
| `OnlyPresentation_MayReferenceGodot`, `ApplicationAndPersistence_CannotWriteAuthoritativeState`, `StateAssemblies_HoldNoStaticMutableState`, `ViewsAndEvents_HaveNoPublicSetters`, `PresentationSource_NeverReachesPastThePublicReadAndCommandSurface`, `TheRegistry_OwnsIdentityOnly_AndDependsOnNothingButDomain`, `TheWorldStateWriter_IsNotVisibleOutsideDomain` | Architecture.Tests | Phase-1 architecture rules hold, unchanged |
| `KinematicsTests`, `TerrainGridTests`, `TierRulesTests` (`tests/Domain.Tests/Spatial/SpatialTests.cs`) | Domain.Tests | Movement, terrain and tier rules are unchanged |
| `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture` | Persistence.Tests | Fixture policy, unchanged |
| `--smoke`, `--quit-after 300`, `--ui-shots`, `--delta-shots` | Godot runs | No Phase-1 runtime regression |
