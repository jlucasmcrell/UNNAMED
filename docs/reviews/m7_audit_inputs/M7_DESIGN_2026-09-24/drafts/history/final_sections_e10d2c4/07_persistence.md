## 7. Persistence and save migration (schema 14)

This section owns every persisted shape M7 adds, the one migration step, the load pipeline changes, the digests, the fixtures and the save-compatibility proofs. The records' behaviour is owned by sections 3-5; their names, owners and guards by section 6. Line citations are to `e10d2c4`.

### Decisions

- Schema 13 → 14 in one step, `SchemaV13ToV14`, with one v14 fixture. No new section file; `SaveFormat.Current` (1), `CheckedFiles`, `worldgen_version` and the RNG contract do not change.
- `player.msgpack` gains `factions` and a `route` on every companion. `entities.msgpack` gains `pieces`, `structure_seq` and `npc_errands`. `cells.msgpack` and the manifest do not change.
- Every new field is nullable in its DTO and required on decode ("corrupt, not defaulted"). Only the migration step supplies defaults.
- Shape is checked at decode; values and cross-record references are checked at apply, where a failure rejects one row.
- A route is decoded only through `NavRoute`'s validating factories, so an invalid route is a decode failure for either host.
- `Sections/SchemaV13.cs` freezes `V13.Player`, `V13.Companion` and `V13.EntitiesSection`. Four sites are repointed, including `Migrations.cs:701`.
- `DecodeEntitySection` returns a `DeltaSnapshot`. The loader, the definition-ID pass and the rebase copy it with `with`.
- Every `PlayerRecord.With*` carries `Factions`, and both companion copy sites carry `Route`. Reflection guards catch the next omission.
- The digest tags become `unnamed.player/v10`, `unnamed.effective-cell/v2` and `unnamed.simulation/v2`. No digest is persisted, so no save byte moves.
- The definition-ID pass covers piece `def_id`, errand `npc_id`, act `subject`, knowledge `knower` and `via`, and standing `faction_id`. It never resolves a `container.pce_*` key.
- A standing merge that sums to 0 drops the row with a Warning. An `ArgumentException` thrown by the pass becomes a Blocker, never a crash.
- A storage piece dropped by the pass spills its chest to the ground. An errand at a dropped station becomes `to_home`.
- Knowledge rows keep `source`, `via` and `identity` as string keys. M7 writes only `reported`/`identified` and stores no other witness field.
- There is no baseline transition, no deadfall and no `M6LayoutFingerprint`. `container.timber_stack` is region layout, outside every baseline hash.
- One real M6 save is committed at the end of E0. It is written on the M7 branch by the unchanged M6 code.
- The writer pack is `content-0.1.7`. The current fixture pack goes from 0.2.8 to 0.2.9 with two renames. A later lint that rejects the pack bumps it to 0.2.10.
- `StateDump` gains one replayable mask, for `container.pce_…` keys. Every other new field reaches the dump by reflection.
- Redundant saved copies are cross-checked on load: an act's `cell_key` against its position, and an errand's `work_owner` against its piece's owner.

### 7.1 Why a schema bump is necessary

- **New keys in two section shapes:** `PlayerDto` (`SectionCodec.cs:21-63`), `CompanionDto` (`:75-90`) and `EntitiesSectionDto` (`:203-218`). Schemas 3-13 each bumped for an additive persisted field, and the fixture policy makes the bump mechanical (`tests/Persistence.Tests/Fixtures/README.md:12-19`).
- **Required-on-decode needs a version boundary.** A schema-14 save without `pieces` is corrupt; a schema-13 one is merely old. An "optional with default" field would let a buggy writer silently drop every piece and the whole ledger.
- **DATA_MODEL §6 rule 1** ("additive changes with a default are migration-free", `docs/DATA_MODEL.md:850`) contradicts the tested practice; M7 corrects it (§7.16).

**Unchanged:** `save_format` (no file is added, so `SaveIntegrity.TryReadRoot`'s every-`CheckedFiles` rule, `SaveLoader.cs:469`, is never sprung); `worldgen_version`, the RNG contract and the running fingerprint (M7's only new world object, `container.timber_stack`, is a `RegionLayout.Containers` entry, and only terrain and fixed nodes reach the generator, `GameSession.cs:104-107`).

### 7.2 Persisted fields

Units: `mm` means integer world millimetres, absolute unless stated. `tick` means a 20 Hz world tick. `mdeg` means millidegrees in [0, 360 000), where 0 is +Z. Enum-like values are snake_case string keys, never ordinals.

| File | DTO | Field | Type | Units / domain | Required from | Default for schema ≤ 13 (written by the step) | Owner (slice) |
|---|---|---|---|---|---|---|---|
| player | `PlayerDto` | `factions` | `FactionsDto?` | the ledger | 14 | `{next_act_seq: 1, acts: [], knowledge: [], standing: []}` | `FactionSystem` (`Factions`) |
| player | `FactionsDto` | `next_act_seq` | `long` | ≥ 1; the next act's `seq` | 14 | 1 | " |
| player | `FactionsDto` | `acts` | `ActDto[]` | ascending `seq`, each `1 ≤ seq < next_act_seq`; at most `acts.log_capacity` (256) rows, kept by `FactionRules.Compact` (content, so not checked on load) | 14 | `[]` | " |
| player | `ActDto` | `seq`, `kind`, `subject`, `cell_key`, `x_mm`, `z_mm`, `tick` | `long`, `string`, `string`, `string`, `long`, `long`, `long` | kind ∈ {`creature_killed`, `switch_set`}; subject is a definition ID; position is the player's body in mm; `cell_key` = `CellKey.OfWorld(x_mm/1000.0, z_mm/1000.0)` | 14 | - | " |
| player | `FactionsDto` | `knowledge` | `KnowledgeDto[]` | sorted by (`knower` ordinal, `act`), with unique pairs | 14 | `[]` | " |
| player | `KnowledgeDto` | `knower`, `act`, `identity`, `source`, `via`, `tick`, `delta` | `string`, `long`, `string`, `string`, `string?`, `long`, `int` | knower is a `faction.*` ID; act is present in `acts`; identity ∈ {`identified`, `unidentified`}; source ∈ {`reported`, `witnessed`}; `via` is the member NPC (`npc.*`) or nil; `delta` is in standing points and is 0 while unidentified | 14 | - | " |
| player | `FactionsDto` | `standing` | `StandingDto[]` | sorted by `faction_id`, unique; zero is never stored | 14 | `[]` | " |
| player | `StandingDto` | `faction_id`, `points` | `string`, `int` | points in [-1000, 1000], ≠ 0 | 14 | - | " |
| player | `CompanionDto` | `route` | `NavRouteDto?` | the committed route | 14 | `{status: "none", goal_mm: [0,0], corners_mm: [], planned_tick: 0, stamp: 0, watch_mm: [0,0,0,0], partial: false}` | `CompanionSystem` (`Companions`) |
| both | `NavRouteDto` | `status` | `string` | `none` \| `active` \| `unreachable` | 14 | `none` | the route's host |
| both | `NavRouteDto` | `goal_mm` | `long[2]` | mm; what the route was planned for | 14 | [0, 0] | " |
| both | `NavRouteDto` | `corners_mm` | `long[]` | flat x, z pairs in mm; 1-32 pairs when `active`, 0 otherwise; consumed corners are removed | 14 | [] | " |
| both | `NavRouteDto` | `planned_tick` | `long` | tick | 14 | 0 | " |
| both | `NavRouteDto` | `stamp` | `ulong` | opaque window stamp; a cache key, never resolved | 14 | 0 | " |
| both | `NavRouteDto` | `watch_mm` | `long[4]` | min x, min z, max x, max z in mm; min ≤ max | 14 | [0, 0, 0, 0] | " |
| both | `NavRouteDto` | `partial` | `bool` | - | 14 | false | " |
| entities | `EntitiesSectionDto` | `pieces` | `PieceDto[]?` | sorted by `instance_id` ordinal | 14 | `[]` | `BuildingSystem` (`Structures`) |
| entities | `PieceDto` | `instance_id` | `string` | `pce_<ULID>` = `EntityId.Derived(Piece, ordinal, "unnamed.piece/v1", owner)` | 14 | - | " |
| entities | `PieceDto` | `def_id` | `string` | a `piece.*` definition ID | 14 | - | " |
| entities | `PieceDto` | `host_cell` | `string` | the cell of the anchor, `CellKey.OfWorld(x_mm/1000.0, z_mm/1000.0)` | 14 | - | " |
| entities | `PieceDto` | `x_mm`, `z_mm` | `long` | the slot anchor, absolute mm | 14 | - | " |
| entities | `PieceDto` | `rotation` | `int` | quarter turns, 0-3 | 14 | - | " |
| entities | `PieceDto` | `owner` | `string` | `chr_<ULID>` | 14 | - | " |
| entities | `PieceDto` | `health` | `int` | points, 1 to `health_max` (`HealthCurrent`) | 14 | - | " |
| entities | `PieceDto` | `door_open` | `bool` | meaningful for door pieces only; false elsewhere | 14 | - | " |
| entities | `EntitiesSectionDto` | `structure_seq` | `long?` | ≥ 0; +1 for every place, dismantle and destroy; never reused | 14 | 0 | `BuildingSystem` |
| entities | `EntitiesSectionDto` | `npc_errands` | `NpcErrandDto[]?` | sorted by `npc_id` ordinal | 14 | `[]` | `NpcSystem` (`NpcErrands`) |
| entities | `NpcErrandDto` | `npc_id` | `string` | an `npc.*` definition ID | 14 | - | " |
| entities | `NpcErrandDto` | `host_cell` | `string` | the cell of the NPC's authored site, fixed for the record's life | 14 | - | " |
| entities | `NpcErrandDto` | `phase` | `string` | `to_work` \| `at_work` \| `to_home` | 14 | - | " |
| entities | `NpcErrandDto` | `piece_id`, `work_owner` | `string?`, `string?` | `pce_…`, `chr_…`. `piece_id` is nil exactly when `to_home`. `to_work`/`at_work` rows carry both, and `work_owner` equals the piece's owner | 14 | - | " |
| entities | `NpcErrandDto` | `x_mm`, `z_mm`, `facing_mdeg` | `long`, `long`, `int` | the NPC's body pose (mm, mdeg) | 14 | - | " |
| entities | `NpcErrandDto` | `route` | `NavRouteDto?` | required on every errand, `none` included | 14 | - | " |
| entities | `NpcErrandDto` | `stuck_ticks` | `int` | ticks, ≥ 0 | 14 | - | " |

The act, knowledge and standing DTOs carry exactly these keys, and `NavRouteDto` is one type shared by both hosts. `PieceDto` and `NpcErrandDto` carry no `baseline_hash`. Their host cells enter the section's `baselines` table, like every other entities record.

**Changed in use, unchanged in shape.** `ContainerDto` (schema 6) also holds piece chests:
- the key is `"container." + pieceId.Value.ToLowerInvariant()`, a valid definition-ID shape;
- the instance ID is `Derived(Container, <piece ordinal>, "unnamed.piece-container/v1", pieceId.Value)`;
- the host cell is the cell of the chest site, proven independently of the piece's host cell.

**New DTO members** in `SectionCodec.cs`, each with the doc comment "Required from schema 14. The 13 -> 14 step gives older saves …":

```csharp
[Key("factions")]      public FactionsDto? Factions { get; set; }         // PlayerDto: "... an empty ledger: no act was recorded before M7"
[Key("route")]         public NavRouteDto? Route { get; set; }            // CompanionDto: "... route none: no one walked a planned route before M7"
[Key("pieces")]        public PieceDto[]? Pieces { get; set; }            // EntitiesSectionDto: "... none"
[Key("structure_seq")] public long? StructureSeq { get; set; }            // EntitiesSectionDto: "... 0"
[Key("npc_errands")]   public NpcErrandDto[]? NpcErrands { get; set; }    // EntitiesSectionDto: "... none"
// New [MessagePackObject] classes: FactionsDto, ActDto, KnowledgeDto, StandingDto, NavRouteDto, PieceDto, NpcErrandDto (keys as the table).
```

**World-side records and properties:**
- `PlayerRecord.Factions`: a `FactionLedger`, `init`, default `FactionLedger.Empty`, validated in its initialiser like `Posture` (`PlayerState.cs:306`). It throws `ArgumentException` on:
  - `NextActSeq < 1`;
  - acts that are not strictly ascending, `Seq ∉ [1, NextActSeq)`, a kind outside `ActKinds.Built`, an invalid subject, or `CellKey ≠ CellKey.OfWorld(XMm/1000.0, ZMm/1000.0).ToString()`;
  - knowledge rows that are unsorted or duplicated, name an act that is absent, have a knower that is not a `faction.*` ID, a key outside its closed set, a `via` that is neither null nor an `npc.*` ID, or a non-zero delta on an unidentified row;
  - standing rows that are unsorted or duplicated, not `faction.*`, 0, or outside [-1000, 1000].
- Domain constants: `FactionLedger.OrdinaryFloor = -999` and `FactionLedger.MaxPoints = 1000`. FAC001 pins `config.factions` to them.
- `CompanionRecord.Route`: a `NavRoute`, `init`, default `NavRoute.None`. The `PlayerRecord` companion loop (`PlayerState.cs:198-205`) also rejects a null `Route`.
- `DeltaSnapshot` (`WorldDelta.cs:188-200`) gains:

```csharp
/// <summary>Player-placed pieces, sorted by instance ID (schema 14).</summary>
public ImmutableArray<PieceRecord> Pieces { get; init; } = ImmutableArray<PieceRecord>.Empty;
/// <summary>The structure sequence: every piece ID's ordinal, and the structure revision (schema 14).</summary>
public long StructureSequence { get; init; }
/// <summary>Named NPCs away from their site, sorted by NPC ID (schema 14).</summary>
public ImmutableArray<NpcErrandRecord> NpcErrands { get; init; } = ImmutableArray<NpcErrandRecord>.Empty;
```

`PieceRecord` and `NpcErrandRecord` are defined in sections 4 and 6.1.9, and `NavRoute` and `FactionLedger` in sections 3 and 5.

**The dump rule.** `StateDump` serialises every public property (`StateDump.cs:32-33`). So `NavRoute`, `FactionLedger`, `ActRecord`, `FactionKnowledge`, `FactionStanding`, `PieceRecord` and `NpcErrandRecord` expose **no computed public instance property**. `NavRoute.Problem()` and the value `Equals`/`GetHashCode` overrides are methods, which the dump does not see.

### 7.3 Derived state: never saved, and where it is rebuilt

| Never saved | Owner | Rebuilt where | Why that is exact |
|---|---|---|---|
| `NavGrid`, tiles, per-tile stamps | `NavigationSystem` (`Navigation`) | `_navigation.Build()` in the constructor; restamped on `RebuildNavigation` | a pure function of content and piece rows (N-A7, G3) |
| Window stamps outside a route | - | recomputed by the follower | saved only inside a route, as a cache key |
| Authoritative and preview `NavScratch`; `NavCounters` | `NavigationSystem`; `Simulation._previewScratch` | allocated lazily on first use | never read for a result (G15) |
| `SystemContext.Space`, closed piece-leaf cache, socket index, `StructureFootprints` | `BuildingSystem` (`Structures`) | `_building.Populate()`; on place, dismantle, destroy; leaf cache alone on a door toggle | rows plus definitions, in `StructureOrder` (G10) |
| `StructureAudit` | `BuildingSystem` (`Structures`) | `_building.Populate()` | a report: the piece lines |
| `ErrandAudit` | `NpcSystem` (`NpcErrands`) | `_npcs.Populate()` | a report: the errand lines; `Simulation.StructureAudit` returns them after the piece lines, in `NpcId` order |
| Piece `health_max`, footprints, bounds, sockets, slot keys, chest site, station anchor | definitions | read at use | content |
| `Simulation.StructureRevision` | - | = the persisted `World.StructureSequence` | a view |
| An errand's goal | `NpcSystem` | the piece row and its definition, or the `NpcSite` | `NavRoute.Goal*` records what was planned for; a moved goal replans |
| Faction tier and level, `FactionSetup.Relevant`, members, `FactionView` | `FactionContent`; views | content build; every read | tiers are never stored, so a ladder retune is not save-locked |
| Registry entries for `pce_` and piece-chest `cnt_` | `WorldDelta.FromSnapshot` | `TryApplyPiece`, `TryApplyContainer` | the registry is never saved |
| Creature homes | `CreatureSystem.Populate` | from `Setup.Layout.Space` only (G9) | homes never depend on pieces |
| Bodies of NPCs not on an errand; cell tiers | `NpcSystem`, `TierSystem` | `Populate`, `Settle` | unchanged |
| Placement previews | presentation | per frame | never authoritative (G7) |
| Unsaved inputs no M7 decision may read: the open conversation, a non-errand NPC's facing, cell tiers, `PlayerCombat` | Phase 1 | Phase 1 | G21, G22, the `"dead"`-only refusal (section 6) |

**Saved on purpose, because the next tick depends on them and nothing reproduces them:** a piece door's open state, every mover's committed route with its stamp, an errand NPC's pose and stuck count, and `StructureSequence`.

**Load order.** The table maps each M7 addition onto PERSISTENCE §7.4's steps as the code runs them.

| §7.4 step | Code | M7 change |
|---|---|---|
| a, b | `SaveLoader.Run` (`SaveLoader.cs:45-97`) | none: the same four files and the same integrity root |
| c | migration chain (`:99-129`) | + `SchemaV13ToV14` |
| l (runs before d) | decode (`:142-155`) | entities decode returns a `DeltaSnapshot`; the player decode reads `factions` and `route` |
| d | `ResolveDefinitions` (`:158-159`, `:206-359`) | §7.10; an `ArgumentException` becomes a Blocker |
| e | generator contract | none |
| f | `ProveBaselines` (`:361-408`) | piece and errand host cells (§7.7) |
| g-j | `WorldDelta.FromSnapshot` (`WorldDelta.cs:531-576`) | order: `StructureSequence`, cells, entities, created, **pieces**, containers, creatures, **errands** |
| k | the `Simulation` constructor, the single rebuild point for a new game and a load | `_effects.Seed` → `_building.Populate()` → `_navigation.Build()` → `_npcs.Populate(player.Companions)` → `_companions.Populate()` → `_creatures.Populate()` → `_tiers.Settle()` (section 6.1.2) |
| m | the `TryApply*` checks, then the content-aware audits inside the `Populate`s | §7.7 and the audit list below |
| n | none | construction and load publish no M7 event (G27) |

A new game takes the same constructor path with an empty `WorldDelta`: sequence 0, no pieces or errands, `FactionLedger.Empty`, no companions.

**Load audits** (in the `Populate`s; they publish nothing; `BuildingSystem.Populate` writes the piece lines to `StructureAudit` and `NpcSystem.Populate` the errand lines to `ErrandAudit`, both shown in the `StructureAudit` view; player work is kept and reported, never silently discarded):
- a piece now off-lattice, outside every build area, over authored or protected ground, or unsupported (its provider dropped by the pass) is **kept**;
- `health` above a retuned `health_max` is clamped; `door_open` on a non-door is treated as false;
- an errand whose piece is not an intact station becomes `to_home` (route `None`, stuck 0); an `at_work` errand off its anchor becomes `to_work`;
- an errand whose NPC's site moved to another cell is re-hosted, and its `BaselineHash` is restamped at the next `TakeSnapshot` (the created-row rule);
- an errand whose NPC is no longer placed, or is in `player.Companions` (G23), is dropped.

### 7.4 The `SchemaV13ToV14` step

**Freeze first.** Create `src/Persistence/Sections/SchemaV13.cs`, namespace `UNNAMED.Persistence.Sections.V13`, with a frozen-file header in the `SchemaV12.cs:1-7` form:

> "The player shape schema 13 wrote (12 -> 13 writes it, 13 -> 14 reads it), the companion shape schemas 12-13 wrote, and the entities shape schemas 9-13 wrote (8 -> 9 writes it). The v9-v13 fixtures pin them. Their parts that schema 14 did not change are the current DTOs (InventoryDto, ProgressionDto, DiscoveryDto, EquipmentDto, EffectDto, RelationshipDto, ConversationDto, QuestDto, PostureDto, EntityDto, CreatedDto, CellBaselineDto, ContainerDto, CreatureDto); the step that next changes one of those must freeze a copy of it first."

| Frozen type | Exact copy of | Keys |
|---|---|---|
| `V13.Player` | `PlayerDto` (`SectionCodec.cs:21-63`) | the 18 keys `instance_id` … `posture`; `companions` typed `V13.Companion[]?` |
| `V13.Companion` | `CompanionDto` (`:75-90`) | the 11 keys `npc_id` … `trail_mm` |
| `V13.EntitiesSection` | `EntitiesSectionDto` (`:203-218`) | `records`, `created`, `baselines`, `containers?`, `creatures?` |

**Repoint.** Each site keeps its wire bytes; the v8-v13 fixtures prove it.

| Site | Today | After |
|---|---|---|
| `Migrations.cs:566` (`SchemaV8ToV9`) | `new EntitiesSectionDto { … }` | `new V13.EntitiesSection { … }` (the same five keys) |
| `Migrations.cs:701` (`SchemaV11ToV12`) | `Companions = Array.Empty<CompanionDto>()` | `Array.Empty<V13.Companion>()`. The next row forces it; without it, the line stops compiling |
| `Sections/SchemaV12.cs:32` | `CompanionDto[]? Companions` | `V13.Companion[]? Companions`. The header (`:4-7`) drops `CompanionDto` from its current-DTO list and names `V13.Companion` |
| `Migrations.cs:722` (`SchemaV12ToV13`) | `new PlayerDto { … }` | `new V13.Player { … }`; `old.Companions` is already `V13.Companion[]?` |

The headers of `SchemaV8.cs` and `SchemaV11.cs` are brought up to date as needed (the 12 → 13 precedent of header-only edits). `V8.EntitiesSection` keeps `CreatureDto` (`SchemaV8.cs:46`), which schema 14 does not change.

**The step** (in `Migrations.cs`, with `using V13 = UNNAMED.Persistence.Sections.V13;`):

```csharp
/// <summary>
/// Schema 13 to 14 (M7): the player gains a faction ledger and each companion a route; the world gains placed pieces, the
/// structure sequence and NPC errands. No act was recorded before M7, nothing was built or sent to work, and no one walked a
/// planned route.
/// </summary>
public sealed class SchemaV13ToV14 : SchemaMigration
{
    public override int From => 13;
    public override string Summary =>
        "schema 13 -> 14: the player gains a faction ledger (empty before M7) and each companion a route (none); " +
        "the world gains placed pieces, the structure sequence and NPC errands (none before M7)";

    public override void Apply(MigrationDocument document, MigrationEnvironment environment, MigrationReport report)
    {
        var options = SectionCodec.MessagePackOptions;
        if (document.Sections.GetValueOrDefault(SaveFormat.Player) is { } player)
        {
            var old = MessagePackSerializer.Deserialize<V13.Player>(player, options);
            document.Sections[SaveFormat.Player] = MessagePackSerializer.Serialize(new PlayerDto
            {
                InstanceId = old.InstanceId, Name = old.Name, XMm = old.XMm, YMm = old.YMm, ZMm = old.ZMm,
                AppearanceSeed = old.AppearanceSeed, Inventory = old.Inventory, Progression = old.Progression,
                FacingMdeg = old.FacingMdeg, Discoveries = old.Discoveries, Equipment = old.Equipment, Currency = old.Currency,
                Effects = old.Effects, Relationships = old.Relationships, Conversations = old.Conversations, Quests = old.Quests,
                Companions = old.Companions?.Select(c => new CompanionDto
                {
                    NpcId = c.NpcId, Order = c.Order, Condition = c.Condition, XMm = c.XMm, ZMm = c.ZMm, FacingMdeg = c.FacingMdeg,
                    Health = c.Health, DownedTick = c.DownedTick, StuckTicks = c.StuckTicks, LastCombatTick = c.LastCombatTick,
                    TrailMm = c.TrailMm,
                    Route = new NavRouteDto { Status = "none", GoalMm = new long[2], CornersMm = Array.Empty<long>(),
                                              PlannedTick = 0, Stamp = 0, WatchMm = new long[4], Partial = false },
                }).ToArray(),
                Posture = old.Posture,
                Factions = new FactionsDto { NextActSeq = 1, Acts = Array.Empty<ActDto>(),
                                             Knowledge = Array.Empty<KnowledgeDto>(), Standing = Array.Empty<StandingDto>() },
            }, options);
        }
        if (document.Sections.GetValueOrDefault(SaveFormat.Entities) is { } entities)
        {
            var old = MessagePackSerializer.Deserialize<V13.EntitiesSection>(entities, options);
            document.Sections[SaveFormat.Entities] = MessagePackSerializer.Serialize(new EntitiesSectionDto
            {
                Records = old.Records, Created = old.Created, Baselines = old.Baselines,
                Containers = old.Containers, Creatures = old.Creatures,
                Pieces = Array.Empty<PieceDto>(), StructureSeq = 0, NpcErrands = Array.Empty<NpcErrandDto>(),
            }, options);
        }
        document.Manifest["schema_version"] = To;
        report.Steps.Add(Summary);
    }
}
```

Rules: the step is pure (no files, no definition resolution, `Migrations.cs:49-53`); a quarantined section passes through as null; a null nullable stays null and decode refuses it (the step never repairs corruption); the route default is written inline, so freezing `NavRouteDto` later needs only a type rename; pre-M7 acts are never reconstructed from flags or corpses. Register it by appending `new SchemaV13ToV14()` to `SchemaMigrations.Production` (`Migrations.cs:69-81`) and setting `SaveFormat.SchemaVersion = 14` (`SaveModel.cs:20`).

### 7.5 The codec: shape at decode, values at apply

| Check | Where | On failure |
|---|---|---|
| A missing `factions`, companion `route`, `pieces`, `structure_seq`, `npc_errands` or errand `route` | decode | `FormatException("<file> has no <key> (required from schema 14)")`, or "companion {npc} has no route …", or "npc errand {npc} has no route …" |
| An unknown key: phase, route status, act kind, identity, source | decode | `FormatException` naming the key |
| Array shape: `goal_mm` of length 2, `watch_mm` of length 4, `corners_mm` of even length ≤ 64 | decode | `FormatException` |
| Route invariants (`NavRoute.Problem()`) | decode, through the factories | the factory's `ArgumentException`, rethrown as `FormatException("{what} has an invalid route: …")` |
| Unparseable instance IDs (`instance_id`, `owner`, `piece_id`, `work_owner`) | decode (`EntityId.Parse`) | `FormatException` |
| `structure_seq < 0` | decode | `FormatException` |
| Ledger order, ranges and cross-checks | the `PlayerRecord.Factions` initialiser | `ArgumentException` |
| Piece, errand and piece-chest values and references | `TryApply*` (§7.7) | that row alone is rejected |

A player-section failure of either exception type becomes `SaveCorruptionException` (`SaveLoader.cs:147-155`). An entities-section decode failure quarantines the section (`:410-430`, which already catches `ArgumentException`).

**Routes.** `NavRoute` has a private constructor and is built only through `NavRoute.None`, `NavRoute.Active(goal, corners, plannedTick, stamp, watch, partial)` and `NavRoute.Unreachable(goal, plannedTick, stamp, watch)`, each throwing when `Problem() != null`. The private helper `SectionCodec.Route(NavRouteDto? dto, string what)` checks the shape, then calls the factory named by `status` (`none` requires every other field zero or empty; `unreachable` requires no corners and `partial == false`); `RouteDto(NavRoute)` writes the keys back. **An invalid route is a decode failure for both hosts:** every runtime write goes through a factory (`WorldDelta.SetNpcErrand`, the companion state writes), so only a corrupt or buggy writer produces one, which is the quarantine class.

**Encode.** `EncodePlayer` (`:327-386`) writes `Factions` and each companion's `Route`. In `EncodeEntities` (`:478-549`), `Prove`'s third parameter becomes `string label` (`:481`, since an errand has no `EntityId`); the loop adds `Prove(p.HostCell, p.BaselineHash, p.InstanceId.Value)` per piece and `Prove(e.HostCell, e.BaselineHash, $"npc errand {e.NpcId}")` per errand, without which a cell hosting only a piece would have no `baselines` entry; and it writes `Pieces`, `StructureSeq = snapshot.StructureSequence` and `NpcErrands` in canonical order.

**Decode.** `DecodePlayer` (`:396-432`) reads `factions` and each companion's `route` per the table. `DecodeEntitySection` (`:555-596`) becomes `public static DeltaSnapshot DecodeEntitySection(byte[] bytes)`, returning `new DeltaSnapshot(ImmutableArray<CellDeltaRecord>.Empty, entities) { Created, Containers, Creatures, Pieces, StructureSequence, NpcErrands }` with each piece and errand stamped `baselines.GetValueOrDefault(HostCell)`. `DecodeEntities` (`:598`) stays `DecodeEntitySection(bytes).Entities`; the tuple deconstructions at `MigrationTests.cs:148`, `:179` and `:197` switch to property access.

### 7.6 The hand-listed copy traps

**`DeltaSnapshot` construction sites.** There are four. A fifth, `tests/World.Tests/WorldDeltaTests.cs:194`, is positional and compiles unchanged.

| Site | After | Failure if missed |
|---|---|---|
| `SaveLoader.cs:143-146` (decode) | `var delta = DecodeOrQuarantine(sections[SaveFormat.Entities], SaveFormat.Entities, SectionCodec.DecodeEntitySection, quarantined, report, DeltaSnapshot.Empty) with { Cells = cells };` | every load loses the three properties |
| `SaveLoader.cs:356-358` (definition pass) | `delta with { Cells, Entities, Created, Containers, Creatures, Pieces, NpcErrands }` | on every content update, `StructureSequence` resets to 0. `TryApplyPiece`'s ordinal check then rejects every piece; without that check, the next placement would re-mint `Derived(Piece, 1, …)` and `CreateEntity` would throw |
| `BaselineTransitions.cs:112-117` (rebase) | `delta with { Cells, Entities, Created, Containers, Creatures, Pieces, NpcErrands }` | the same, whenever a transition applies |
| `WorldDelta.cs:520` (`TakeSnapshot`) | adds `Pieces`, `StructureSequence = _structureSequence` and `NpcErrands` | nothing is saved |

Guard G11 (section 6) builds on the v14 fixture snapshot: it asserts by reflection that no public `DeltaSnapshot` property is empty in the input (failing with "extend the builder" when one is added), encodes and decodes, runs the pass under a content identity whose hash differs, runs `SemanticRebase.Apply` over every host cell with the fixture generator on both sides, and asserts every property equal element by element.

**`PlayerRecord.With*`.** The pass ends in `player.WithInventory(…)…WithCompanions(…)` (`SaveLoader.cs:356-357`). All seven `With*` methods (`PlayerState.cs:222-253`) build a new record and hand-list `{ Posture = Posture }`; **each becomes `{ Posture = Posture, Factions = Factions }`**, or one omission resets the ledger on every content update. Guard `EveryPlayerRecordInitProperty_SurvivesEveryWithMethod` runs on a test-built `PlayerRecord` with a non-empty ledger and a non-default posture (E2 commit 1), and again on the v14 fixture player (commit 3): after asserting that `Posture` and `Factions` are non-default, it applies every public `With*` with an identity argument and asserts every public property equal.

**Runtime carry sites.** Companion routes have the same trap in `CompanionSystem.Populate` (`Companions.cs:135-141`, into `CompanionState`) and `CompanionSystem.Records` (`:149-155`); both carry `Route`. The ledger is seeded by the `RuntimeState` constructor beside `Posture` (`RuntimeState.cs:102`) and captured by `CaptureRecord` (`Factions = _state.Factions`, `Simulation.cs:342-351`); in E2 `FactionSystem` only claims `StateSlice.Factions` and has no `Seed`. Tests `ACompanionRoute_SurvivesPopulateAndCapture` and `TheFactionLedger_SurvivesStartAndCapture` boot a `Simulation` holding an active route and a non-empty ledger, and assert both equal in `CaptureRecord()`.

### 7.7 World-side apply, baseline proof and rebase

**`TakeSnapshot`** writes pieces sorted by ID and errands sorted by `NpcId`, each stamped with `Baseline(host).Digest` as created rows are. It also writes `StructureSequence`. Pieces and errands add no cell record and no dirty reason.

**`FromSnapshot`** runs in this order: `SetStructureSequence`, cells, entities, created, pieces, containers, creatures, errands. Pieces go before containers and errands, which reference them. Rejections are `RejectedRecord("entities", key, reason)`.

**`TryApplyPiece`** (key = instance ID) checks, in order:
1. the host cell parses and its hash matches;
2. the ID kind is `Piece`;
3. `DefId` is a valid `piece.*` ID;
4. the owner kind is `Character`;
5. rotation is 0-3;
6. health is ≥ 1;
7. `HostCell == CellKey.OfWorld(XMm/1000.0, ZMm/1000.0).ToString()`;
8. `1 ≤ InstanceId.Timestamp ≤ StructureSequence`, with a comment that the timestamp is read as the derivation ordinal (the D-04 note);
9. the ID has not been seen or registered.

It then calls `CreateEntity(DefinitionId.Parse(DefId), InstanceId)`. It does **not** check that the ID equals `Derived(…, Owner)`, so a later ownership transfer is a row edit, not a re-key.

**`TryApplyNpcErrand`** (key = `npc_id`) checks:
- the host cell parses and its hash matches;
- `NpcId` is a valid `npc.*` ID;
- the phase invariants hold: `to_work`/`at_work` carry both `PieceId` and `WorkOwner`, and `to_home` carries no `PieceId`;
- facing is in [0, 360 000), and stuck is ≥ 0;
- for `to_work`/`at_work`, the named piece is present, and `WorkOwner` equals that piece's `Owner` (a redundant-copy cross-check);
- at most one `to_work`/`at_work` row names a piece; a later duplicate, by `NpcId`, is rejected.

It registers nothing: NPC identities are derived and registered by `NpcSystem.Populate`.

**`TryApplyContainer`** gains one clause for a `container.pce_*` key:
- the piece `"pce_" + key[14..].ToUpperInvariant()` must be present, else "its chest is gone";
- `InstanceId` must equal `Derived(Container, piece.InstanceId.Timestamp, "unnamed.piece-container/v1", piece.InstanceId.Value)`, else "not its chest's identity".

**`ProveBaselines`** (`SaveLoader.cs:361-408`) adds these checks after the creatures loop:

```csharp
foreach (var record in delta.Pieces)     Check(record.HostCell, record.BaselineHash);
foreach (var record in delta.NpcErrands) Check(record.HostCell, record.BaselineHash);
```

A piece is proven against its anchor's cell only. The other cells a straddling piece covers hold nothing of it, and its footprint is content. A piece chest is proven by the existing containers loop, against its site's cell.

**`SemanticRebase.Apply`** (`BaselineTransitions.cs:94-117`) carries pieces and errands as they are, exactly like created instances:

```csharp
var rebasedPieces  = delta.Pieces.Select(p => cellsToRebase.Contains(p.HostCell) ? p with { BaselineHash = baseline(CellKey.Parse(p.HostCell)).Digest } : p).ToImmutableArray();
var rebasedErrands = delta.NpcErrands.Select(e => cellsToRebase.Contains(e.HostCell) ? e with { BaselineHash = baseline(CellKey.Parse(e.HostCell)).Digest } : e).ToImmutableArray();
return delta with { Cells = …, Entities = …, Created = …, Containers = …, Creatures = …, Pieces = rebasedPieces, NpcErrands = rebasedErrands };
```

Legality after a rebase is the load audit's concern (§7.3). The authored layout is outside every `baseline_hash`, so pieces over a later layout edit are audited, not proven. `M7_STATUS` records this.

### 7.8 Digests and `CanonicalState`

**`PlayerRecord.Digest` → `unnamed.player/v10`** (`PlayerState.cs:323-368`, tag at `:328`). Every v9 term keeps its place. Two insertions:
- Inside each companion, after its trail marks: status key, goal x, goal z, planned tick, stamp (`ulong`), watch min x, min z, max x, max z, partial (`bool`), corner count, then each corner as x, z.
- After posture: `NextActSeq`; the act count, then each act as (seq, kind, subject, cell key, x, z, tick); the knowledge count, then each row as (knower, act, identity, source, `via ?? "-"`, tick, delta); the standing count, then each row as (faction, points).

**`WorldDelta.EffectiveCellDigest` → `unnamed.effective-cell/v2`** (tag at `WorldDelta.cs:587`). Every v1 term keeps its place. After the population loop and before `Finish`:
- `PiecesIn(cell).Count`, then each piece in ID order: id, def, x, z, rotation, owner, health, `door_open`;
- `NpcErrandsIn(cell).Count`, then each errand in `NpcId` order: npc, phase key, `piece ?? "-"`, `owner ?? "-"`, x, z, facing, the route terms above, stuck.

Counts are hashed even when they are zero. A straddling piece is hashed once, in its host cell. That is exact, because `StateDigest` covers every region cell.

**`Simulation.StateDigest` → `unnamed.simulation/v2`** (`Simulation.cs:357-364`): tag, `WorldTick`, `CaptureRecord().Digest`, `World.StructureSequence`, cell count, then the cells as before.

**Nothing persisted moves:** no digest is written to a save (the load proof in `rotation.json` is the integrity-root digest), `RegionDigest` and the probe pin hash baselines only, and no test pins a tag. Coverage is proven by G12 (section 6), whose only exemptions are the proof stamps `PieceRecord.BaselineHash` and `NpcErrandRecord.BaselineHash`: they are outside the digest, as `CreatedEntityRecord.BaselineHash` is outside effective-cell v1.

**`CanonicalState.Render`** (`tests/Persistence.Tests/CanonicalState.cs`) is hand-written, so every new field is written explicitly. Enum values use their save keys, and `ulong` values use `WorldSeed.Format` (`"0x"` plus 16 hex digits).
- **Inside each companion, after `trail_mm`:** a `route` object with `status`, `goal_mm` [x, z], `corners_mm` (flat), `planned_tick`, `stamp`, `watch_mm` [4] and `partial`.
- **In `player`, after `posture`:** a `factions` object with:
  - `next_act_seq`;
  - `acts` [{`seq`, `kind`, `subject`, `cell_key`, `x_mm`, `z_mm`, `tick`}];
  - `knowledge` [{`knower`, `act`, `identity`, `source`, `via` or null, `tick`, `delta`}];
  - `standing` [{`faction_id`, `points`}].
- **At the root, after `creatures`:**
  - `structure_seq`;
  - `pieces` [{`instance_id`, `def_id`, `host_cell`, `baseline_hash`, `x_mm`, `z_mm`, `rotation`, `owner`, `health`, `door_open`}];
  - `npc_errands` [{`npc_id`, `host_cell`, `baseline_hash`, `phase`, `piece_id` or null, `work_owner` or null, `x_mm`, `z_mm`, `facing_mdeg`, `route` {as above}, `stuck_ticks`}].

### 7.9 `StateDump`

The raw dump needs no change. The three `DeltaSnapshot` properties, `PlayerRecord.Factions` and `CompanionRecord.Route` are public properties of the two serialised roots.

**The replayable mask.** A piece-chest key, `container.pce_<ulid lower-case>`, embeds an ID salted with the owner's fresh `chr_`, and the instance-ID pattern `^[a-z]{3}_[0-9A-Z]{26}$` (`StateDump.cs:93-94`) does not match it, so two fresh runs that place a chest would differ in `Containers[*].Key`. Add `[GeneratedRegex("^container\\.pce_([0-9a-z]{26})$")] PieceChestKey()`: `MaskIds` turns a match into `"container.#"`, and `Name` into `"container." + Named("pce_" + ulid.ToUpperInvariant())`, so key and piece share one name (`container.pce#3`). Test: F-E10. Recorded limit: `Order` sorts arrays of objects by content (`:97-120`), so route corners and trail marks are order-checked only by the raw dump and the digests.

**Field-count growth.** `Compare` counts one leaf per scalar (nulls included), one per missing key and one per array-length mismatch. Empty arrays add nothing (`StateDump.cs:49-89`).

| Added | Leaves |
|---|---|
| `world.StructureSequence` | 1, always |
| `player.Factions` | 1 (`NextActSeq`) + 7 per act + 7 per knowledge row + 2 per standing row |
| a `NavRoute` (companion or errand) | 10 (`Status`, `GoalXMm`, `GoalZMm`, `PlannedTick`, `Stamp`, `Partial`, and the 4 `Watch` values) + 2 per corner |
| a piece | 10 (`InstanceId`, `DefId`, `HostCell`, `XMm`, `ZMm`, `Rotation`, `Owner`, `HealthCurrent`, `BaselineHash`, `DoorOpen`) |
| an errand | 10 + its route |
| a piece chest | 4 + 4 per stack (the existing container shape) |

**Expected counts.**
- `ASaveAndALoad_CompareEqual_FieldByField` (a new game, no companion) gains exactly 2 leaves (`StructureSequence`, `NextActSeq`); its `> 100` assertion is unchanged.
- `--playthrough-verify` in E2: 415 + 1 + 1 + 10 (Tavar's `None` route) = **427**.
- From E3: + 37 for the ledger (two acts, three knowledge rows: Waystation–armour, Survey–heart, Survey–armour; one standing row, Waystation +100, since the Survey ends at 0) = 464, plus about 28 for Kera's wares record materialised by the `m7_tell_kera` purchase (4 + 4 per stack × 6 stacks), the bought billet, and a few dialogue-memory and relationship rows: **about 500**. From E5: about 540 (§13.8).
- The criterion is **0 differences**, not the number. `M7_STATUS` records each measured count with this breakdown; a gap from the estimate is explained, never rounded.

### 7.10 The definition-ID pass (`SaveLoader.ResolveDefinitions`)

It runs only when `content_hash` differs (`SaveLoader.cs:158`). Every rewritten list is rebuilt through an ordinal sorted collection, so canonical order survives a rename. The M7 rows run after the existing ones: pieces (with the spill and the errand fix-up), errands, acts, knowledge, standing. The local `Resolve(id, referencedBy)` role strings are `$"piece {id} in cell {host}"`, `$"npc errand {npc}"`, `$"player act {seq}"`, `$"player faction knowledge of act {act}"` and `"player faction standing"`.

| Stored ID | Renamed or replaced | Discarded | Two resolve to one |
|---|---|---|---|
| `PieceRecord.DefId` | rewritten; a family change is the audit's to report | the piece is dropped, with a Loss line; a storage piece spills and an errand at the piece is re-homed (below) | n/a: keyed by instance ID |
| `NpcErrandRecord.NpcId` | rewritten | the errand is dropped, with a Loss line | the first by original `NpcId` is kept; the other gets Loss "npc errand {old}: merged into {new}'s; its pose is dropped" |
| `ActRecord.Subject` | rewritten | the act and its knowledge rows are dropped; standing is kept; `NextActSeq` never decreases | n/a: acts are distinct rows |
| `FactionKnowledge.Knower` | rewritten | the row is dropped, with a Loss line | union by (`knower`, `act`): identified beats unidentified, then the lower tick |
| `FactionKnowledge.Via` | rewritten, with `CountAlias`/`CountReplacement` | **the row is kept with `via = null`**, a `CountDiscard`, and a **Warning** (not Loss): "knowledge of act {seq} by {knower}: its reporter '{npc}' was removed; the knowledge stays". An unresolved `via` is a **Blocker**, with `Resolve`'s wording | rewritten |
| `FactionStanding.FactionId` | rewritten | the row is dropped, with a Loss line | points are summed, then clamped to [`OrdinaryFloor` -999, `MaxPoints` 1000]. **A sum of 0 drops the row with the Warning "standing with {a} and {b} merged to neutral"** |
| `container.pce_*` keys | **never resolved**: a layout-style key, like authored container keys (`:271-281` resolves only items) | - | - |
| Instance IDs (`pce_`, `cnt_`, owners, `WorkOwner`, `PieceId`); every `NavRoute` field | untouched: they are not definitions. A stale `stamp` only causes a replan | - | - |

`Via` calls `content.Resolve` directly, because the local `Resolve`'s Discarded branch always writes Loss "dropped"; the direct call keeps the local bookkeeping (alias and replacement counts; an unresolved ID is a Blocker).

**Spill for a dropped storage piece.** The container item pass runs first, so the chest's items carry resolved definitions and discarded items have already left with a Loss line. Then the chest's `ContainerRecord` (key `"container." + id.Value.ToLowerInvariant()`) is removed, and each remaining item becomes a `CreatedEntityRecord` keeping its `itm_` ID, resolved definition, count and quality, with the piece's `HostCell` and `BaselineHash` and the cell-relative position `((int)WorldMath.FloorMod(XMm / 10, CellSizeCm), (int)WorldMath.FloorMod(ZMm / 10, CellSizeCm))` of the piece anchor (the dropped definition's chest site is unknown). Warning: "{n} stacks from the chest of piece {id} were put on the ground". This mirrors destroy's spill; without it `TryApplyContainer` would reject the orphaned chest and lose its items.

**Errand fix-up for a dropped station.** A `to_work`/`at_work` errand naming the dropped piece becomes `to_home` with `PieceId = null`, `Route = NavRoute.None` and `StuckTicks = 0`, keeping its pose and `WorkOwner`. Warning: "npc errand {npc}: its work place {id} was removed; they walk home". Without it, `TryApplyNpcErrand` would reject the row and the NPC would jump to its site. Other dependents of a dropped piece (a door on a dropped doorway, walls on a dropped pad) are kept and reported by the load audit.

**It ends with:**

```csharp
return (player.WithInventory(…)…WithCompanions(…) with { Factions = ledger },
        delta with { Cells = cells, Entities = …, Created = created /* + spilled, re-sorted */, Containers = containers /* − spilled chests */,
                     Creatures = …, Pieces = pieces, NpcErrands = errands });
```

**An exception from the pass is a Blocker.** At `SaveLoader.cs:158-159`:

```csharp
if (manifest.ContentHash != context.Content.Hash)
{
    try { (player, delta) = ResolveDefinitions(player, delta, context.Content, report); }
    catch (ArgumentException e) { report.Blockers.Add($"the definition-ID pass could not rebuild the save: {e.Message}"); }
}
```

A load then refuses with its report (`SaveCompatibilityException`), and `save:migrate --dry-run` prints it. Neither crashes. The standing zero-merge rule removes the one known path; the catch is defensive, and `ADefinitionPassThatBreaksARecord_IsABlocker_NotACrash` drives it with a hand-built content identity that aliases `faction.fixture.keepers` to `npc.fixture.smith`, so the rebuilt ledger fails its `faction.*` check.

### 7.11 Content packs and the probe mirror

**Writer pack `tests/Persistence.Tests/Fixtures/content-0.1.7/`**: a full copy of `content-0.1.6/` plus the definitions the v14 records name and the three configs. Writer packs need not pass today's checks; only their hash matters.

| File | ID and content |
|---|---|
| `pieces/fixture/{pad,old_wall,doorway,door,chest}.yaml` | `piece.fixture.pad`, `piece.fixture.old_wall`, `piece.fixture.doorway`, `piece.fixture.door`, `piece.fixture.chest`: section 4's timber shapes and costs; `health_max` 200, 200, 200, 120 and 100 |
| `factions/fixture/keepers.yaml`, `delvers.yaml` | `faction.fixture.keepers` (reaction `creature_killed creature.beast.wolf_grey +100`); `faction.fixture.delvers` (`creature_killed creature.beast.wolf_grey -100`, `switch_set world.lever.mill_gate +100`); both seated at `location.wolf_den` (the 0.1.x name) |
| `npcs/fixture/smith.yaml` | `npc.fixture.smith`, `faction_ref: faction.fixture.delvers`, with no `companion:` block |
| `regions/fixture_vale.yaml` | gains `npcs: [{ npc_ref: npc.fixture.smith, position_m: [32, 70], facing_deg: 0 }]` |
| `items/material/timber.yaml` | `item.material.timber` |
| `config/{building,factions,navigation}.yaml` | the game's M7 values |

- `M2Fixtures.Historical.WriterContentVersion` becomes `"0.1.7"`, and `WriterContentHash` the computed hash (`tests/M2.Probe/M2Fixtures.cs:109-110`).
- `TheWritersContentIdentity_IsItsFixturePack` (`MigrationTests.cs:751-757`) loads `"content-0.1.7"`.

**Current fixture pack `Fixtures/content`, 0.2.8 → 0.2.9.**
- **Adds:**
  - `piece.fixture.pad`, `piece.fixture.wall` (the renamed old wall), `piece.fixture.doorway`, `piece.fixture.door`, `piece.fixture.chest`;
  - `faction.fixture.keepers` and `faction.fixture.diggers` (the renamed delvers), both seated at `location.den_mouth`;
  - `npc.fixture.smith` (`faction_ref: faction.fixture.diggers`), placed in `regions/fixture_vale.yaml` at (32, 70) m. That is 2 m from `location.den_mouth`'s anchor (30, 70), inside its 8 m radius and the region's 100 m bounds, so FAC-M1 holds;
  - `item.material.timber`, `config.building`, `config.factions` and `config.navigation`.
- **`_aliases.yaml`** gains `piece.fixture.old_wall: piece.fixture.wall` and `faction.fixture.delvers: faction.fixture.diggers`. Its header gains "0.2.9 added the M7 definitions and renamed the old wall and the delvers".
- **Constraints the pack puts on lints** (reported to sections 3 and 4): NAV004 and NAV005 compare against the built `MovementRules`, not raw `base_speeds` keys (the fixture copy omits the optional `stand_height_m`); the BLD lints do not tie an ID's second segment to `family` (fixture pieces are `piece.fixture.*`). When a lint rejects the pack, the pack is fixed, never the lint.
- **Version constants:** `Fixtures.ContentVersion = "0.2.9"` (`HistoricalFixtureTests.cs:16`); `M2Fixtures.Historical.CurrentContentVersion = "0.2.9"` with `CurrentContentHash` the computed hash (`M2Fixtures.cs:252-253`); the `CurrentContent()` mirror (`:259-283`) gains 12 IDs in ordinal order (`config.building`, `config.factions`, `config.navigation`, `faction.fixture.diggers`, `faction.fixture.keepers`, `item.material.timber`, `npc.fixture.smith`, `piece.fixture.chest`, `piece.fixture.door`, `piece.fixture.doorway`, `piece.fixture.pad`, `piece.fixture.wall`) and both aliases, pinned by `TheProbesContentMirror_IsTheFixtureContentPack` (`MigrationTests.cs:735-745`).
- **The 0.2.10 rule.** The pack lands in E2 and passes every check that exists then. If a later lint (FAC001 in E3; BLD001-BLD009 in E5, E7, E8 and E9) rejects it, the pack's content is fixed under 0.2.10 (0.2.11 for a second such slice), and the same commit updates `Fixtures.ContentVersion`, `CurrentContentVersion`, `CurrentContentHash`, the mirror, the `_aliases.yaml` header and README policy 4. No `expected.json` changes (it records no content identity), and this is not STOP S1 or S2.

### 7.12 The v14 fixture and `expected.json`

`M2Fixtures.Historical.Player()` and `World(Registry)` gain the rows below, through World's internal mutators (`src/World/World.csproj:21`). Piece IDs are `EntityId.Derived(Piece, n, "unnamed.piece/v1", owner.Value)`; the foreign owner is `EntityId.Create(Character, 1_700_000_000_200, [5,5,5,5,5,5,5,5,5,5])`. `M2.Probe fixture <dir>` writes it at world tick 5 000, and `<dir>/quick` is copied to `Fixtures/v14/quick`. Every record lies in the ten fixture cells, so `CellsMatched` stays 10.

**Player.**

| Field | v14 value (writer IDs; names after load in brackets) | Proves |
|---|---|---|
| Warden companion `route` | active; goal (150 250, -40 125); corners (149 250, -41 000), (149 750, -40 750), (150 250, -40 125); planned tick 4 990; stamp `0x0123456789ABCDEF`; watch (129 000, -61 000, 171 000, -20 000); **partial true** | every route field non-default, in the player section |
| `next_act_seq` | 3 | the sequence survives |
| act 1 | `creature_killed`, `creature.beast.wolf_grey`, `r_0_0:c_00_02`, (20 000, 250 000), tick 4 100 | an act with a creature subject |
| act 2 | `switch_set`, `world.lever.mill_gate`, `r_0_0:c_00_07`, (50 000, 750 000), tick 4 200 | an act with a flag subject that **no faction knows** |
| knowledge | (`faction.fixture.delvers` [`diggers`], act 1, `identified`, `reported`, via `npc.fixture.smith`, tick 4 300, delta -100); (`faction.fixture.keepers`, act 1, `identified`, `reported`, via `npc.fixture.warden` [`warden_sera`], tick 4 150, delta +100) | **the same act moves two factions in opposite directions**, in stored form; renames reach `knower` and `via`. The warden is not a keepers member: the row is crafted to prove the `via` rename |
| standing | (`delvers` [`diggers`], -100), (`keepers`, +100) | a rename reaches standing |

**World.** The pieces sit around the z = 500 m seam, which lattice squares straddle because 500 m is not a multiple of 3 m. All are owned by Aelin unless stated.

| seq | def (writer) | anchor (mm) | r | host | health | `door_open` | notes |
|---|---|---|---|---|---|---|---|
| 1 | `piece.fixture.pad` | (49 500, 499 500) | 0 | `c_00_04` | 200 | false | its square (z 498-501 m) **straddles the c_00_04 / c_00_05 seam** |
| 2 | `piece.fixture.doorway` | (49 500, 498 000) | 0 | `c_00_04` | 200 | false | |
| 3 | `piece.fixture.door` | (49 500, 498 000) | 0 | `c_00_04` | 120 | **true** | |
| 5 | `piece.fixture.old_wall` [`piece.fixture.wall`] | (49 500, 501 000) | 0 | **`c_00_05`** | **150** | false | a structure hosted in two cells; damaged; renamed on load |
| 7 | `piece.fixture.chest` | (49 500, 499 500) | 0 | `c_00_04` | 100 | false | its chest site (49 500, 500 400) is in **`c_00_05`** |
| 9 | `piece.fixture.pad` | (61 500, 499 500) | **1** | `c_00_04` | 200 | false | **foreign owner** |

- **`structure_seq`** is 9; the gaps at 4, 6 and 8 show that the sequence is not a count.
- **The chest container:** key `"container." + chest7.Value.ToLowerInvariant()`, ID `Derived(Container, 7, "unnamed.piece-container/v1", chest7.Value)` (registered like the other fixture containers), host `r_0_0:c_00_05`, one stack of `item.material.timber` ×3 at quality 0.
- **The errand:** `npc.fixture.smith`, host `r_0_0:c_00_00` (the cell of his authored site (32, 70) m), `to_home`, no piece, work owner Aelin, pose (40 000, 120 000) facing 180 000, stuck 3. Route active: goal (32 000, 70 000); corners (36 000, 95 000), (32 000, 70 000); planned tick 4 980; stamp `0xFEDCBA9876543210`; watch (12 000, 50 000, 60 000, 140 000); partial false. This is the route DTO inside `entities.msgpack`.

**`expected.json`.** Write `v14/expected.json` and regenerate `v1..v13/expected.json` in one run: `UNNAMED_WRITE_FIXTURE_EXPECTATIONS=1 dotnet test tests/Persistence.Tests --filter "FullyQualifiedName~Fixture_LoadsToItsExpectedCurrentState"`. Review every older diff line by line. It must be exactly:
- in `player`, a comma on the line closing `posture`, then the 6-line `factions` object (`next_act_seq: 1`, three empty arrays);
- at the root, a comma on the line closing `creatures`, then the 3 lines `"structure_seq": 0`, `"pieces": []`, `"npc_errands": []`;
- **v12 and v13 only:** a comma on the line closing the warden's `trail_mm`, then the 17-line `route` object (`status "none"`, zero arrays, `planned_tick 0`, `stamp "0x0000000000000000"`, `partial false`).

Nothing else may change, not a value, an order or a key. A larger diff is a migration bug (STOP S4), and the fix is never to the expectation. Also unchanged: the bytes of `v1..v13/quick/**` and `content-0.1.0 … 0.1.6`; `worldgen_profile.json` and `M2Fixtures.Profile()`; every frozen type's wire shape; the older alias expectations; `CellsMatched` and the empty `Loss`; `.gitattributes` line 3.

**Test edits the bump forces.**
- `HistoricalFixtureTests`: the alias arm `>= 12` (`:241`) becomes `12 or 13`; a new `>= 14` arm is the ≥ 12 list with `npc.fixture.warden -> npc.fixture.warden_sera x4` (2 relationships, 1 companion, 1 `via`), plus `faction.fixture.delvers -> faction.fixture.diggers x2` (1 `knower`, 1 standing) and `piece.fixture.old_wall -> piece.fixture.wall`, in ordinal order; and the M7 block (§7.15, F-E2).
- `MigrationTests`: `:78` 11 → 12 plus `Steps[11]` `"schema 13 -> 14:"`; each prefix list (`:104-105`, `:124-125`, `:142`, `:162`, `:178`, `:194`, `:214`, `:230`, `:246`) gains `"schema 13 -> 1"`; `:262`'s `Assert.Single` becomes the two-step list; `:398` 12 → 13 plus `Steps[12]`; `:428` reads "… to schema 14"; `:148`, `:179`, `:197` use property access.
- Unchanged and still valid: `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture`, `TheProductionChain_HasOneStepPerVersion_InOrder`, and the kill-mid-migration probe test (now over v14).

### 7.13 Quarantine and corruption behaviour

| Damage | Behaviour | Test |
|---|---|---|
| `entities.msgpack` fails its hash or its decode (a missing required list, an unknown key, a malformed or invalid route, an unparseable ID, `structure_seq < 0`) | The whole section is quarantined: pieces, sequence, errands, piece chests, other containers, creatures and created instances are lost together, and the warning names the section. `IsComplete` is false, so `SaveStore.Migrate` refuses; an in-game load proceeds with the warning | `AQuarantinedEntitiesSection_LosesStructuresErrandsAndChestsTogether_AndSaysSo` |
| One decodable but invalid piece or errand row: rotation 4, health 0, host cell not its anchor's, ordinal 0 or above the sequence, a duplicate ID, an errand at a missing piece, `work_owner` not the piece's owner, a second worker at a piece, `to_home` with a piece, facing out of range, negative stuck | That row alone is rejected ("invalid entities record '{key}' dropped: {reason}"); a chest or errand whose piece was rejected is rejected in turn | `AnInvalidPieceRow_IsRejectedAlone`, `AnInvalidErrandRow_IsRejectedAlone`, `APieceChestWithoutItsPiece_IsRejected`, `APieceChestWithTheWrongIdentity_IsRejected` |
| `structure_seq` lost with its section | The world restarts at 0 with no pieces; the next placement mints ordinal 1 into a registry holding no piece, so nothing collides | the quarantine test |
| `cells.msgpack` quarantined, entities intact | Pieces, `door_open` and errands load (their proofs use the entities section's own `baselines`); authored door flags are lost, as today | `ACellsQuarantine_LeavesPiecesAndErrandsWhole` |
| `player.msgpack` corrupt, including a missing or invalid `factions` or companion `route`, or a ledger cross-check failure | Fatal `SaveCorruptionException`; backups are offered, never loaded automatically | the corrupt-not-defaulted tests below |
| The definition-ID pass builds an invalid record | A Blocker naming the pass; the load refuses with its report | `ADefinitionPassThatBreaksARecord_IsABlocker_NotACrash` |
| Cross-section references | None: errands, chests and doors reference pieces in the same section; the player section holds no world instance ID. "No NPC is both a companion and on an errand" is enforced at `Populate` (G23) | by construction |

**Corrupt-not-defaulted tests** (the pattern at `MigrationTests.cs:270-278`, `:348-356`):
- `ASchema14PlayerWithoutFactions_IsCorrupt_NotDefaulted`. Further cases: an unknown `identity`; unsorted acts; an act whose `cell_key` is not the cell of its position; a standing row of 0.
- `ASchema14CompanionWithoutARoute_IsCorrupt_NotDefaulted`. Further cases: status `"wandering"`; odd-length `corners_mm`; 33 corners; `watch_mm` of length 3; an `active` route with no corners.
- `ASchema14EntitiesSectionWithoutPiecesSeqOrErrands_IsCorrupt_NotDefaulted`, one case per key.
- `AnErrandWithoutARoute_IsCorrupt_NotDefaulted`. Further case: an `unreachable` route with corners.

### 7.14 Historical saves

**The v13 fixture.** `Schema13To14_GivesNothingBuiltNoLedgerAndNoRoutes` migrates `Fixtures.Copy(13)`, then decodes.
- **Asserted empty:** `Pieces`, `NpcErrands`; `StructureSequence == 0`; `Factions == FactionLedger.Empty`; the warden's route is `NavRoute.None`.
- **Asserted kept:** everything schema 13 held, including the warden's trail, the crouched posture, the quests and the corpse container.
- **Asserted not reconstructed:** the ledger is empty although the fixture world holds a wolf corpse and `world.lever.mill_gate = 3`.

**The committed real M6 save**, `tests/Application.Tests/Saves/m6_hollow/quick/**`.
- **Written at the end of E0 on the M7 branch.** E0 changes no code, so `src/` and `content/` are still `e10d2c4`'s and the save is exactly what M6 writes. There is no scratch worktree.
- **The writer** is a one-off test, run once and not kept in the build (the v1 precedent, `Fixtures/README.md:97-120`): `Arena.OpenCreatures(session, session.Setup, (44, 138), 0, [], r => r.WithCompanions([Tavar following at (45, 138)]), seed: 42, keepSpawns: true)`; walk `StateDumpTests.Played`'s waypoints (opening `door.longhouse`), then open `door.forge_shed`; move `item.tool.water_flask` into `container.waystation_chest`; save with `SaveStore.Save(SaveSlots.Quick, SaveDocuments.Capture(world, CaptureRecord(), session.Content, tick, playtime))` (the `CompanionTests.cs:294-296` pattern).
- **A `README.md` beside it** holds the writer's source, the commit it ran on, and the manifest's `schema_version` (13), content hash and `worldgen_fingerprint`. `.gitattributes` gains `tests/Application.Tests/Saves/*/quick/** binary`. The save is never edited; outside `Fixtures/`, the fixture-count test does not see it.

**The test**, `AnM6Save_LoadsIntoM7_WithNothingBuiltNeutralStandingNoErrandAndNoRoute` in `tests/Application.Tests/HistoricalSaveTests.cs`, copies the save into a `TempProfile` (the repository root found as at `Harness.cs:24`) and calls `session.Load("quick")`. It lands in E2 and grows in E3 and E5.

| Slice | Asserts |
|---|---|
| E2 | `Report.SourceSchema == 13`, and `Report.Steps` is exactly one step starting `"schema 13 -> 14:"` |
| E2 | the manifest's `worldgen_fingerprint` equals `session.Generator.Fingerprint`, which proves M7 changed no generator input |
| E2 | `CellsRebased`, `CellsMismatched`, `Blockers`, `Loss` and `Aliases` are empty |
| E2 | `simulation.World.Pieces` is empty; `simulation.World.StructureSequence == 0`; `World.NpcErrand("npc.ashen_hollow.kera_voss")` is null; Kera stands at her site; the navigation grid digest equals a new game's |
| E2 | both door flags are set, and `container.waystation_chest` holds the flask |
| E2 | Tavar is a companion with `Route == NavRoute.None` and his saved trail; `CaptureRecord().Factions == FactionLedger.Empty` |
| E2 | a `session.Save("quick")` leaves `pre_migration_13_quick` byte-identical to the committed data (`SaveStore.cs:296`); a reload gives 0 `StateDump.Compare` differences |
| E3 | every `FactionView` has 0 points, tier `neutral` and level 0; `Acts` is empty |
| E3 | Kera's billet row is withheld from `Simulation.Wares`, and Sel's `notes` reply is not offered |
| E5 | `container.timber_stack` has no record and shows its table's 80 `item.material.timber` (four stacks of 20) |
| E5 | `StructureAudit` is empty |
| E5 | `Simulation.Pieces` is empty and `StructureRevision == 0` |

### 7.15 Field-by-field acceptance evidence

| # | Test | Project | Slice | Compares | Criterion |
|---|---|---|---|---|---|
| F-E1 | `T01_M7State_RoundTripsEveryField_ByteStable` (`RoundTripTests.cs`) | Persistence | E2 | the v14 fixture player and world through `SaveStore.Save`/`Load`: `PlayerRecord.Digest`, `M2Fixtures.WorldDigest` (now over effective-cell v2), and a resave of `player.msgpack` and `entities.msgpack` | equal digests and byte-identical sections (T-01, T-03) |
| F-E2 | `Fixture_LoadsToItsExpectedCurrentState(14)` and `Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState(1..14)` | Persistence | E2 | `CanonicalState` against `expected.json`, **plus an M7 block that states the values independently of `expected.json`** | see below |
| F-E3 | G11 (section 6) | Persistence | E2 | §7.6 | no hand-listed copy drops a property |
| F-E4 | `EveryPlayerRecordInitProperty_SurvivesEveryWithMethod` | Persistence | E2 | §7.6 | every init property survives every `With*` |
| F-E5 | G12 `EveryPersistedField_MovesItsDigest` (section 6) | World | E2 | changing each public property in turn, except the proof stamps `PieceRecord.BaselineHash` and `NpcErrandRecord.BaselineHash` (stamped by `TakeSnapshot` and at decode, and outside the digest as `CreatedEntityRecord.BaselineHash` is outside effective-cell v1) | every such change moves `EffectiveCellDigest` or `PlayerRecord.Digest`; the test names its exemption list explicitly |
| F-E6 | `ASaveAndALoad_CompareEqual_FieldByField` (existing, +2 leaves) and `ABuiltStaffedAndKnownWorld_CompareEqual_FieldByField` (new) | Application | E2; E9 | `StateDump.Compare` before the save against after a fresh `GameSession` load. The new test starts from a record holding the P5 ledger (two acts, three knowledge rows, Waystation +100), runs the Crossing Workshop command table (section 4) through step 6, and stores 2 timber in the chest: 19 pieces, Kera `at_work`, Tavar waiting | 0 differences; ≥ 265 leaves beyond a new game (190 pieces + 20 errand + 8 chest + 10 route + 37 ledger) |
| F-E7 | `Building_RoundTripsThroughSave_AndNavigationDerivesIdentically` (section 4) | Application | E5 | rows, sequence, dump, **grid digest**, `Space.Blockers` element-wise | derived state is rebuilt identically |
| F-E8 | N-A6 `MidRoute_SaveLoad_GoesOnTheSame` (section 3); `SaveThenContinue_EqualsContinue_WithFactions` (section 5); the G21 and G22 tests (section 6) | Application | E4, E3, E9 | `StateDigest` every 50 ticks after a mid-route save | persisted mover state is complete |
| F-E9 | `--playthrough-verify` | Godot | E2, E3 | the §7.9 compare: 427, then about 500 | 0 differences; the count is recorded |
| F-E10 | `TwoFreshRuns_WithAPieceChest_ProduceTheSameReplayableDump` | Application | E8 | the replayable dumps of two fresh new games that take timber from the stack, place a pad and a chest, and store 2 timber | equal dumps; `container.pce#` present |
| F-E11 | `ANewGame_TakesTimber_BuildsAPadAndAWall_AndLoadsEqual` | Application | E5 | a new game takes timber from `container.timber_stack`, places a pad and a wall in the build area, saves, and loads through `GameSession` | 0 `StateDump` differences (the new-game building proof) |

**F-E2's M7 block** asserts every §7.12 value by value at schema 14: the six piece IDs (`Derived(Piece, {1, 2, 3, 5, 7}, …, Aelin)`, `Derived(Piece, 9, …, foreign)`); seq 5 renamed to `piece.fixture.wall` with health 150 in `c_00_05`; seq 3 open; seq 9's owner and rotation 1; `StructureSequence == 9`; the chest's key, derived `cnt_`, host and timber ×3; the smith's errand and 2-corner route; the warden's 3-corner partial route; the ledger with `diggers` and `npc.fixture.warden_sera` renamed. At schema < 14 it asserts no pieces, sequence 0, no errands, `FactionLedger.Empty` and every companion route `NavRoute.None`. The block exists because `CanonicalState` omissions are invisible to the `expected.json` it generates.

**Related, owned by section 4:** `TwoHundredPieces_RoundTripAndStayNavigable` (RK-06) runs through a content-hash change (so the pass and the `with` copies run), bounds `entities.msgpack` growth at 200 × 250 bytes, and asserts doorway-to-interior plans after the load.

### 7.16 Document updates (E2 commit 5 unless stated)

| Document | Change |
|---|---|
| `docs/PERSISTENCE.md` §2 (`:72-90`) | E0 writes the navigation row (ruling 1). E2 adds §7.3's other rows and fixes "§7.4 step g" → "step k" (`:86`, and `:366` in §6.2) |
| §3.2 (`:106-121`) | mark `companions`, `buildings`, `journal`, `command_log`, `orphans` not built; as built, four files hold M7's lists inside `player` and `entities`; a new section file needs a schema-aware integrity root first |
| §5.1 (`:186-200`) | add "Factions (schema 14)" and "Routes (schema 14)"; replace the stale "Implemented so far (schema 7)" paragraph with the schema-14 list |
| §5.3 (`:221-247`) | add `pieces`, `structure_seq`, `npc_errands` (absolute-mm anchors, host cells, proof) and piece chests as schema-6 containers with derived keys and IDs |
| §5.4 (`:248-252`) | as built: no structure record; `pce_` rows in `entities.msgpack`; retired by dismantle or destroy; a quarantine names the section |
| §6.2 chain table (`:344-357`) | add rows 11 → 12, 12 → 13 and 13 → 14 |
| §6.4 | M7 adds no transition (containers are layout); a renewable node later brings its own transition and frozen fingerprint |
| §7.4 (`:499`) | as-implemented note: the `FromSnapshot` order and the constructor's rebuild order (§7.3) |
| §10 | map the F-E and corrupt-not-defaulted tests to T-01, T-03, T-13, T-16, T-21 (the bounded act log) and T-23 |
| `docs/DATA_MODEL.md` §6 (`:846-850`) | rule 1 corrected: every added persisted field bumps the schema and is required on decode. Save-sensitive: the `Derived` tags `unnamed.piece/v1` and `unnamed.piece-container/v1` (a change re-keys every piece); `config.building.module_m` (absolute anchors go off-lattice on a retune, audited and never migrated). Not save-locked: the ladder (tiers derived) and `config.navigation.node_m` (routes in mm) |
| `tests/Persistence.Tests/Fixtures/README.md` | §7.12's world-table rows; provenance `v14/ \| The M7 schema-14 writer \| M2.Probe fixture <dir> (writes content identity 0.1.7)`; policy 4's 0.1.7 and 0.2.9 (and 0.2.10) narrative |
| `AGENTS.md` "Where the code is" (E0) | `tests/Application.Tests/Saves/` holds committed game saves; never edit them |
| `docs/M7_STATUS.md` (E2, completed in E10) | fields, frozen shapes, fixture, the expected-diff review, digest tags, the M6 save's provenance, the measured dump counts with breakdown, and the recorded limits (layout outside the baseline; `Order` erases corner order) |

### 7.17 Where it lands

- **E0:** the M6 save, its README and the `.gitattributes` line (a fourth commit, after the documents).
- **E1:** `NavRoute`, with its factories and value equality.
- **E2**, five commits: (1) records, digests, the claim-only `FactionSystem`, G12 and F-E4, green with no save change; (2) the freeze and repoints, byte-identical; (3) the bump, DTOs, codec, loader, packs, probe, v14 fixture, every expectation, F-E4's v14-fixture-player case, G11 (with `<InternalsVisibleTo Include="Persistence.Tests" />` added to `src/Persistence/Persistence.csproj` and `SaveLoader.ResolveDefinitions` changed from private to internal, because G11 calls it and `SemanticRebase.Apply` from Persistence.Tests) and the corrupt, quarantine and pass tests, as one commit because the fixture-count test is red between its halves; (4) the M6-save test's E2 rows; (5) the documents.
- **E3:** the M6-save faction rows; `--playthrough-verify` at about 500; the 0.2.10 rule if FAC001 rejects the pack. **E5:** the M6-save building rows; F-E11; the 0.2.10 rule if a BLD lint rejects the pack. **E8:** the `StateDump` mask and F-E10. **E9:** F-E6's new test. **E10:** the final counts in `M7_STATUS`.

After E2, schema 14's shape is frozen for M7: a new persisted field is STOP S2.

### 7.18 Not in M7

| Item | Belongs to |
|---|---|
| Witness fields beyond `source`/`via`/`identity`: witness position, facing, a full witness list, per-NPC `knower` rows | M9 (the witnessed channel) |
| A renewable timber node, with its own `BaselineTransition` and frozen layout fingerprint (the deadfall, `M6LayoutFingerprint` and their two tests are dropped) | the milestone that wants renewable timber (M9 or later) |
| A world-global act log with an `actor` field (acts by NPCs) | when NPC acts arrive (M9 or later) |
| A persisted open conversation (`conversation_open`); the walking-errand talk refusal (G21) makes it unnecessary | not scheduled |
| Persisting `CellTiers`, or hysteresis-free authoritative gates | before M9 |
| A ULID-keyed structure record or `buildings.msgpack` (PERSISTENCE §5.4) | the settlement milestone (M10) |
| A schema-aware integrity root that allows new section files | the first milestone that needs a new section file |
| Crime, bounty and pardon records | Phase 3 (owner Q2) |

### Tests owned by this section

| Test | Project | What it proves |
|---|---|---|
| `T01_M7State_RoundTripsEveryField_ByteStable` (F-E1) | Persistence.Tests | Every schema-14 field round-trips with equal digests, and resaves byte-identically (T-01, T-03) |
| `Fixture_LoadsToItsExpectedCurrentState` (extended to 14, with the M7 block) | Persistence.Tests | Every fixture loads to its expected state; v14's values and older fixtures' empty M7 defaults are stated independently |
| `Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState` (1..14) | Persistence.Tests | All 14 fixtures migrate through the commit path |
| `EveryPlayerRecordInitProperty_SurvivesEveryWithMethod` (F-E4) | Persistence.Tests | No `With*` drops `Factions`, `Posture` or a later init property |
| `Schema13To14_GivesNothingBuiltNoLedgerAndNoRoutes` | Persistence.Tests | The step's defaults, with no reconstruction of pre-M7 acts |
| `ASchema14PlayerWithoutFactions_IsCorrupt_NotDefaulted` | Persistence.Tests | A missing or invalid ledger is corruption, including the act cell cross-check |
| `ASchema14CompanionWithoutARoute_IsCorrupt_NotDefaulted` | Persistence.Tests | A missing or invalid companion route is corruption |
| `ASchema14EntitiesSectionWithoutPiecesSeqOrErrands_IsCorrupt_NotDefaulted` | Persistence.Tests | Each required entities key is enforced |
| `AnErrandWithoutARoute_IsCorrupt_NotDefaulted` | Persistence.Tests | Every errand carries a valid route, `none` included |
| `AQuarantinedEntitiesSection_LosesStructuresErrandsAndChestsTogether_AndSaysSo` | Persistence.Tests | Quarantine is whole-section and reported |
| `AnInvalidPieceRow_IsRejectedAlone` | Persistence.Tests | A bad piece row is rejected alone |
| `AnInvalidErrandRow_IsRejectedAlone` | Persistence.Tests | A bad errand row is rejected alone, including the `work_owner` cross-check |
| `APieceChestWithoutItsPiece_IsRejected` | Persistence.Tests | The chest clause's presence check |
| `APieceChestWithTheWrongIdentity_IsRejected` | Persistence.Tests | The chest clause's derived-identity check |
| `ACellsQuarantine_LeavesPiecesAndErrandsWhole` | Persistence.Tests | Entities records prove themselves without `cells.msgpack` |
| `PiecesAndErrands_GoThroughTheDefinitionPass_RenamesRemovalsSpillsAndMerges` | Persistence.Tests | §7.10 for pieces and errands, including the spill and the errand fix-up |
| `TheFactionLedger_GoesThroughTheDefinitionPass_RenamesMergesAndRemovals` | Persistence.Tests | §7.10 for acts, knowledge, `via` and standing, including the clamp |
| `TwoFactionsMergedWithOppositeStanding_LoadNeutral_WithAWarning` | Persistence.Tests | A merge that sums to 0 drops the row with a Warning and does not crash |
| `AnUnmappedVia_IsABlocker` | Persistence.Tests | An unresolved `via` blocks, as any unresolved ID does |
| `ASpilledChestItemWithARenamedDefinition_LandsRenamed` | Persistence.Tests | The spill uses the container pass's resolved items, keeping item IDs |
| `ADefinitionPassThatBreaksARecord_IsABlocker_NotACrash` | Persistence.Tests | An `ArgumentException` from the pass becomes a Blocker in both a load and a dry run |
| `PiecesAndErrands_AreProvenByTheirHostCells_AndRebasedByATransition` | Persistence.Tests | Baseline proof and rebase cover the new records |
| `TheWritersContentIdentity_IsItsFixturePack` (now 0.1.7) | Persistence.Tests | The v14 writer's content identity is its pack |
| `TheProbesContentMirror_IsTheFixtureContentPack` (now 0.2.9) | Persistence.Tests | The probe's mirror equals the current pack |
| The `MigrationTests` step-list edits (§7.12) | Persistence.Tests | Chain integrity through 14 |
| `ACompanionRoute_SurvivesPopulateAndCapture` | Application.Tests | Both companion copy sites carry `Route` |
| `TheFactionLedger_SurvivesStartAndCapture` | Application.Tests | The ledger's seed and capture carry it |
| `AnM6Save_LoadsIntoM7_WithNothingBuiltNeutralStandingNoErrandAndNoRoute` | Application.Tests | A real M6 save loads with no transition, empty M7 state and a clean first save |
| `ASaveAndALoad_CompareEqual_FieldByField` (existing; +2 leaves) | Application.Tests | M7 leaves are in the dump |
| `ABuiltStaffedAndKnownWorld_CompareEqual_FieldByField` (F-E6) | Application.Tests | The whole M7 state survives `GameSession` save and load field by field |
| `TwoFreshRuns_WithAPieceChest_ProduceTheSameReplayableDump` (F-E10) | Application.Tests | The piece-chest key mask |
| `ANewGame_TakesTimber_BuildsAPadAndAWall_AndLoadsEqual` (F-E11) | Application.Tests | The new-game building loop saves and loads with 0 differences |
