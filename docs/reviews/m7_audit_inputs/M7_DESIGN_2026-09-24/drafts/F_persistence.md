# M7 Part F - Persistence and save migration: schema 14

Status: the persistence architect's part of the M7 design, 2026-09-24. Design and implementation planning only; not an owner ruling. It implements Part D's §1.5 ("the schema slice, landed once") and D's register rows D1, D13-D15, D19 and D38, and fills in what D, A, B and C leave unstated. Every `path:line` is repo-relative and was re-read in the read-only snapshot of origin/main `e10d2c4`. Where this part refines D, the refinement is named and justified; it never reverses a D decision.

---

## 0. Decisions on one page

1. **One bump, 13 → 14, one step `SchemaV13ToV14`, one v14 fixture.** No new section file, no change to `SaveFormat.Current` (1) or `CheckedFiles` (`src/Persistence/SaveModel.cs:14`, `:31-32`), no `worldgen_version` or RNG-contract change.
2. **player.msgpack** gains `factions` (C's ledger) and `route` on every companion (A). **entities.msgpack** gains `pieces`, `structure_seq` and `npc_errands` (B, with A's route inside each errand). **cells.msgpack and the manifest do not change.**
3. **Every new field is nullable in its DTO and required on decode** ("corrupt, not defaulted"). Only the migration step supplies defaults.
4. **Shape at decode, values at apply.** Missing fields, unknown keys and malformed arrays throw `FormatException` at decode; range and cross-record checks reject one row in `FromSnapshot` instead of quarantining the section (the creature precedent: `src/Persistence/SectionCodec.cs:582-585` vs `src/World/WorldDelta.cs:807-810`). This refines B §9.2.
5. **Freeze:** `Sections/SchemaV13.cs` holds `V13.Player`, `V13.Companion` and `V13.EntitiesSection` (D14). Four sites are repointed: `SchemaV8ToV9` (`Migrations.cs:566`), `SchemaV12ToV13` (`:722`), `V12.Player.Companions` (`Sections/SchemaV12.cs:32`), and **`SchemaV11ToV12`'s `Array.Empty<CompanionDto>()` (`Migrations.cs:701`), which D omits** and which stops compiling once `V12.Player.Companions` changes type.
6. **Hand-listed copies die at the codec.** `DecodeEntitySection` returns a `DeltaSnapshot` instead of a 4-tuple (`SectionCodec.cs:555-596`); the loader, the definition pass and the rebase then use `with`, so `StructureSequence` survives every stage by default (D15, G11).
7. **Digests:** `unnamed.player/v10`, `unnamed.effective-cell/v2`, `unnamed.simulation/v2`, in D §1.5's exact term order. No digest is ever persisted, so the tag bumps touch no save byte.
8. **Definition-ID pass:** piece `def_id`, errand `npc_id`, standing `faction_id`, knowledge `knower` and `via`, act `subject`; never a `container.pce_*` key. A storage piece dropped by the pass **spills its chest to the ground**, like destroy.
9. **Content:** writer pack `Fixtures/content-0.1.7`; the current fixture pack goes from 0.2.8 to 0.2.9 with two renames (`piece.fixture.old_wall → piece.fixture.wall`, `faction.fixture.delvers → faction.fixture.diggers`). The game gains one baseline transition, `"M7: the deadfall by the crossing"`, from a frozen `M6LayoutFingerprint`.
10. **One real M6 game save** is committed as Application test data (`tests/Application.Tests/Saves/m6_hollow/`), written once by the e10d2c4 build: the end-to-end proof that an M6 save boots in M7, and the source of the frozen fingerprint.
11. **`StateDump` gains one masking rule** (a `container.pce_…` key embeds an owner-salted ID). Otherwise the dump picks up every new field by reflection.

---

## 1. (a) Why a schema bump is necessary

A bump is required, not optional:

- **New keys in two section shapes.** `PlayerDto` (`SectionCodec.cs:20-63`), `CompanionDto` (`:74-90`) and `EntitiesSectionDto` (`:202-218`) gain fields. The implemented doctrine bumps the schema for every additive persisted field: schemas 3-13 each did (`docs/PERSISTENCE.md:344-357`; `research/persistence.md` §2). The fixture policy makes the bump mechanical: a changed current shape needs a new `vN/` fixture, frozen previous shapes and a new step (`tests/Persistence.Tests/Fixtures/README.md:12-19`).
- **Required-on-decode needs a version boundary.** A schema-14 save without `pieces` must be corrupt. A schema-13 save without `pieces` is simply old. Only the schema number tells them apart, and only the step may give the old save its defaults (`SectionCodec.cs:399-409` is the pattern). An "optional with default" field would let a buggy writer silently drop every placed structure and the whole faction ledger.
- **DATA_MODEL §6 rule 1** ("additive changes with a default are migration-free", `docs/DATA_MODEL.md:850`) contradicts the implemented and tested practice (`research/persistence.md` §10 item 4). M7 follows the practice and corrects the rule (§13).

What does **not** need a bump:
- **`save_format`.** The layout is unchanged: no file is added, so the integrity-root trap (`SaveLoader.cs:469`; `SaveModel.cs:131-132`) is never sprung.
- **`worldgen_version` / RNG contract.** The deadfall is a fixed node, which is content, carried by a transition (§8.3). The generator code is unchanged, and M7 opens no random channel (D §5.1).

---

## 2. (b) Every new or changed persisted field

Units: mm are integer world millimetres (absolute unless stated), ticks are 20 Hz world ticks, mdeg are millidegrees in [0, 360 000). Enum-like values are snake_case string keys, never ordinals (`SectionCodec.cs:360`, `:373-374` precedent).

### 2.1 `player.msgpack`

| DTO . key | C# type | Units / domain | Required from | Default for schema ≤ 13 (written by the step) | Owner system (slice) |
|---|---|---|---|---|---|
| `PlayerDto.factions` | `FactionsDto?` | - | 14 | `{next_act_seq: 1, acts: [], knowledge: [], standing: []}` | `FactionSystem` (`Factions`) |
| `FactionsDto.next_act_seq` | `long` | sequence, ≥ 1 | 14 | 1 | " |
| `FactionsDto.acts[]` | `ActDto[]` | ascending `seq`, each `1 ≤ seq < next_act_seq` | 14 | `[]` | " |
| `ActDto.seq` / `kind` / `subject` / `cell_key` / `x_mm` / `z_mm` / `tick` | `long` / `string` / `string` / `string` / `long` / `long` / `long` | kind ∈ {`creature_killed`, `switch_set`}; subject a definition ID; cell key parseable; position mm; tick | 14 | - | " |
| `FactionsDto.knowledge[]` | `KnowledgeDto[]` | sorted by (`knower` ordinal, `act`), unique pair | 14 | `[]` | " |
| `KnowledgeDto.knower` / `act` / `identity` / `source` / `via` / `tick` / `delta` | `string` / `long` / `string` / `string` / `string?` / `long` / `int` | knower `faction.*`; act present in acts; identity ∈ {`unidentified`, `identified`}; source ∈ {`witnessed`, `reported`}; via `npc.*` or nil; delta in standing points, 0 while unidentified | 14 | - | " |
| `FactionsDto.standing[]` | `StandingDto[]` | sorted by `faction_id`, unique; zero is never stored | 14 | `[]` | " |
| `StandingDto.faction_id` / `points` | `string` / `int` | points in [-1000, 1000], ≠ 0 | 14 | - | " |
| `CompanionDto.route` | `NavRouteDto?` | - | 14 | `{status: "none", goal_mm: [0,0], corners_mm: [], planned_tick: 0, stamp: 0, watch_mm: [0,0,0,0], partial: false}` | `CompanionSystem` (`Companions`) |
| `NavRouteDto.status` | `string` | `none` \| `active` \| `unreachable` | 14 | `none` | " |
| `NavRouteDto.goal_mm` | `long[2]` | mm: what the route was planned for | 14 | [0, 0] | " |
| `NavRouteDto.corners_mm` | `long[]` | flat x, z pairs, mm, ≤ 32 pairs; consumed corners removed | 14 | [] | " |
| `NavRouteDto.planned_tick` | `long` | tick | 14 | 0 | " |
| `NavRouteDto.stamp` | `ulong` | opaque window stamp (A §11); a cache key, never resolved | 14 | 0 | " |
| `NavRouteDto.watch_mm` | `long[4]` | min x, min z, max x, max z, mm | 14 | [0, 0, 0, 0] | " |
| `NavRouteDto.partial` | `bool` | - | 14 | false | " |

The act, knowledge and standing DTOs are exactly C §14.3's. `NavRouteDto` is exactly A §13.4's.

### 2.2 `entities.msgpack`

| DTO . key | C# type | Units / domain | Required from | Default for schema ≤ 13 | Owner system (slice) |
|---|---|---|---|---|---|
| `EntitiesSectionDto.pieces` | `PieceDto[]?` | sorted by `instance_id` ordinal (= placement order, §9.1) | 14 | `[]` | `BuildingSystem` (`Structures`) |
| `PieceDto.instance_id` | `string` | `pce_<ULID>`, `EntityId.Derived` (D §1.7) | 14 | - | " |
| `PieceDto.def_id` | `string` | `piece.*` definition ID | 14 | - | " |
| `PieceDto.host_cell` | `string` | cell key = `CellKey.OfWorld(x_mm/1000.0, z_mm/1000.0)` of the anchor (`src/World/Coordinates.cs:93-96`) | 14 | - | " |
| `PieceDto.x_mm` / `z_mm` | `long` | the slot anchor, absolute mm (the creature convention, `WorldDelta.cs:162-175`) | 14 | - | " |
| `PieceDto.rotation` | `int` | quarter turns 0..3 | 14 | - | " |
| `PieceDto.owner` | `string` | `chr_<ULID>` | 14 | - | " |
| `PieceDto.health` | `int` | points, 1..`health_max` (the record's `HealthCurrent`) | 14 | - | " |
| `PieceDto.door_open` | `bool` | door pieces only; false elsewhere | 14 | - | " |
| `EntitiesSectionDto.structure_seq` | `long?` | ≥ 0; strictly increases per footprint change; never retired | 14 | 0 | `BuildingSystem` |
| `EntitiesSectionDto.npc_errands` | `NpcErrandDto[]?` | sorted by `npc_id` ordinal | 14 | `[]` | `NpcSystem` (`NpcErrands`) |
| `NpcErrandDto.npc_id` | `string` | `npc.*` definition ID | 14 | - | " |
| `NpcErrandDto.host_cell` | `string` | the cell of the NPC's authored site (fixed for the record's life, D §1.3) | 14 | - | " |
| `NpcErrandDto.phase` | `string` | `to_work` \| `at_work` \| `to_home` | 14 | - | " |
| `NpcErrandDto.piece_id` / `work_owner` | `string?` / `string?` | `pce_…` / `chr_…`; `piece_id` nil exactly when `to_home` | 14 | - | " |
| `NpcErrandDto.x_mm` / `z_mm` / `facing_mdeg` | `long` / `long` / `int` | the NPC's body pose: mm, mdeg | 14 | - | " |
| `NpcErrandDto.route` | `NavRouteDto?` | as §2.1; **required on every errand** (D19) | 14 | - | " |
| `NpcErrandDto.stuck_ticks` | `int` | ticks ≥ 0 (D28) | 14 | - | " |

**Changed in use, unchanged in shape.** `ContainerDto` (`SectionCodec.cs:245-252`, schema 6) now also holds piece chests:
- key `"container." + pieceId.Value.ToLowerInvariant()`, which is a valid definition-ID shape (`src/Domain/DefinitionId.cs:19-20`) and which `TryApplyContainer` registers under (`WorldDelta.cs:788`);
- instance ID `Derived(Container, piece seq, "unnamed.piece-container/v1", pieceId.Value)`;
- host cell = the container site point's cell, proven independently of the piece's (B §14.1).

No new key is needed.

`PieceDto` and `NpcErrandDto` carry no `baseline_hash`. Their host cells enter the section's `baselines` table, as every other record's do (§6.1).

### 2.3 Records and properties on the World side

- `PlayerRecord.Factions` (`FactionLedger`, init, validated like `Posture`, `src/World/PlayerState.cs:306-317`).
- `CompanionRecord.Route` (`NavRoute`, init, default `NavRoute.None`), validated in the `PlayerRecord` constructor's companion loop (`PlayerState.cs:198-205`): `Route is null || Route.Problem() is not null` → `ArgumentException`.
- `DeltaSnapshot` (`WorldDelta.cs:188-200`) gains:

```csharp
/// <summary>Player-placed pieces, sorted by instance ID (schema 14).</summary>
public ImmutableArray<PieceRecord> Pieces { get; init; } = ImmutableArray<PieceRecord>.Empty;
/// <summary>The structure sequence: every piece ID's ordinal, and the structure revision (schema 14).</summary>
public long StructureSequence { get; init; }
/// <summary>Named NPCs away from their place, sorted by NPC ID (schema 14).</summary>
public ImmutableArray<NpcErrandRecord> NpcErrands { get; init; } = ImmutableArray<NpcErrandRecord>.Empty;
```

`PieceRecord` and `NpcErrandRecord` are B §2.2 and D §1.3, verbatim.

**Rule for every persisted record (the StateDump contract, §10.1):** no computed public instance property on `NavRoute`, `FactionLedger`, `PieceRecord`, `NpcErrandRecord` or their row types, because the dump serialises every public property (`src/Application/StateDump.cs:32-33`). `NavRoute.Problem()` stays a method.

---

## 3. (c) Derived state: never saved, and where it comes back

### 3.1 The not-saved table (additions for PERSISTENCE §2)

| Thing | Owner | Rebuilt where | Why it is safe not to save |
|---|---|---|---|
| Navigation grid, tiles, per-tile stamps | `NavigationSystem` (`StateSlice.Navigation`) | constructor `_navigation.Build()`, after `_building.Populate()`; at run time on `RebuildNavigation` | a pure function of content plus the piece rows (A §5.3; test N-A7) |
| Authoritative and preview `NavScratch`, `NavCounters` | `NavigationSystem`, `Simulation._previewScratch` | allocated lazily on the first plan or flood (D36) | never read for a result (A §4; G15) |
| Window stamps outside a route | - | recomputed by the follower | a stamp is saved only inside a route, as a cache key |
| `SystemContext.Space` (rebuilt `WalkSpace`), closed piece-door leaf cache, socket index, `StructureFootprints` | `BuildingSystem` (`Structures`) | constructor `_building.Populate()`; on place, dismantle, destroy and door toggle | derived from rows plus definitions, in `StructureOrder` (D3) |
| `StructureAudit` | `BuildingSystem`, `NpcSystem` | the two `Populate`s | a report, not state |
| Piece `health_max`, footprints, bounds, sockets, slot keys, container site and capacity, station site and work anchor, dependents, the "structure" a piece belongs to | definitions | read at use | content (B §2.2) |
| `Simulation.StructureRevision` | - | equals the persisted `StructureSequence` | a view of a saved value |
| Errand goal (anchor or site) | `NpcSystem` | from the piece row plus its definition, or the `NpcSite` | derived (D §1.3); `NavRoute.GoalXMm/GoalZMm` records what the route was planned for |
| Faction tier, level, `FactionSetup.Relevant`, `MembersOf`, `FactionView`s | `FactionContent.Build`, read time | content build, then on every read | tiers are never stored (C §6.1), so ladder retunes are not baseline-locked |
| Placement preview results | presentation | per frame | never authoritative (G7) |
| Registry entries for pieces and piece chests | `WorldDelta.FromSnapshot` | `TryApplyPiece` / `TryApplyContainer` | the registry is never saved (`research/persistence.md` §5) |
| Creature homes | `CreatureSystem.Populate` | from `Setup.Layout.Space` only, never `SystemContext.Space` (D2, G9) | homes must not depend on pieces |
| Non-errand NPC bodies; cell tiers | `NpcSystem`, `TierSystem` | `Populate`, `Settle` (unchanged) | unchanged doctrine (`src/World/Runtime/Social.cs:103`) |

**Not derived, and deliberately saved:** a piece door's open state (`door_open` on the row), the committed route of every mover (A §11), an errand NPC's pose, and `StructureSequence`. Each is something the next tick depends on that no pure function of content and other saved state can reproduce.

### 3.2 The §7.4 order as the code runs it, with M7's additions

| §7.4 step (`docs/PERSISTENCE.md:484-497`) | Code | M7 change |
|---|---|---|
| a, b | `SaveLoader.Run` (`SaveLoader.cs:45-97`) | none. The same four files, the same integrity root |
| c | the migration chain (`:99-129`) | + `SchemaV13ToV14` |
| (l, early) decode | `:142-155` | entities decode returns a snapshot (§6.1); the player decode reads `factions` and `route` |
| d | `ResolveDefinitions` (`:158-159`, `:206-359`) | §7 |
| e, f | generator contract; `ProveBaselines` (`:163-178`, `:361-408`) | proves piece and errand host cells; the M7 transition (§8.3) |
| g-j | `WorldDelta.FromSnapshot` (`WorldDelta.cs:531-576`) | order: **`StructureSequence`**, cells, entities, created, **pieces**, containers, creatures, **errands** (D §2.10). Pieces precede containers and errands, which reference them |
| k | the `Simulation` constructor (`src/World/Runtime/Simulation.cs:100-145`), the single rebuild point for new game and load | `_effects.Seed` → `_building.Populate()` → `_navigation.Build()` → `_npcs.Populate()` → `_companions.Populate()` → `_creatures.Populate()` → `_tiers.Settle()` (D §1.4) |
| m | per-record `TryApply*`, then the content-aware audits inside the `Populate`s | new: `TryApplyPiece`, `TryApplyNpcErrand`, the chest clause in `TryApplyContainer`; the `StructureAudit` repairs (B §9.6) |
| n | none (`WorldLoaded` is still unbuilt; `docs/M2_STATUS.md:85`) | unchanged: construction and load publish nothing (`tests/Application.Tests/DeterminismAndViewTests.cs:128-143`) |

A new game takes the same constructor path with an empty `WorldDelta`: `StructureSequence` 0, no pieces or errands, `FactionLedger.Empty`, no companions.

---

## 4. (d) The `SchemaV13ToV14` step

### 4.1 Freeze first (README policy 2)

New file `src/Persistence/Sections/SchemaV13.cs`, namespace `UNNAMED.Persistence.Sections.V13`, header in the frozen-file form (`Sections/SchemaV12.cs:1-7`):

> "The player shape schema 13 wrote (12 -> 13 writes it, 13 -> 14 reads it), the companion shape schemas 12-13 wrote, and the entities shape schemas 9-13 wrote (8 -> 9 writes it). The v9-v13 fixtures pin them. Parts schema 14 did not change are the current DTOs (Inventory, Progression, Discovery, Equipment, Effect, Relationship, Conversation, Quest, Posture, Entity, Created, CellBaseline, Container, Creature); the step that next changes one must freeze a copy first."

| Frozen type | Exact copy of | Fields |
|---|---|---|
| `V13.Player` | `PlayerDto` at `SectionCodec.cs:20-63` | the 18 keys `instance_id` … `posture`; `companions` typed `V13.Companion[]?` |
| `V13.Companion` | `CompanionDto` at `SectionCodec.cs:74-90` | the 11 keys `npc_id` … `trail_mm` |
| `V13.EntitiesSection` | `EntitiesSectionDto` at `SectionCodec.cs:202-218` | `records`, `created`, `baselines`, `containers?`, `creatures?` |

Naming follows the README rule "`Sections/V{N-1}`" (`Fixtures/README.md:16-17`) and D14.

### 4.2 Repoint (each keeps its wire bytes; the v8-v13 fixtures prove it)

| Site | Today | After |
|---|---|---|
| `Migrations.cs:566` (`SchemaV8ToV9`) | `new EntitiesSectionDto { … }` | `new V13.EntitiesSection { … }` (same five keys) |
| `Migrations.cs:701` (`SchemaV11ToV12`) | `Companions = Array.Empty<CompanionDto>()` | `Array.Empty<V13.Companion>()`. **Missing from D14**; it is forced by the next row |
| `Sections/SchemaV12.cs:32` | `CompanionDto[]? Companions` | `V13.Companion[]? Companions`; its header drops `CompanionDto` from the current-DTO list and names `V13.Companion` (D14's choice: one frozen companion shared by V12 and V13) |
| `Migrations.cs:722` (`SchemaV12ToV13`) | `new PlayerDto { … Companions = old.Companions, Posture = … }` | `new V13.Player { … }`. `old.Companions` is already `V13.Companion[]?`, so it passes through |
| `Sections/SchemaV8.cs:7`, `SchemaV11.cs:4-7` | header comments | "…and schema 14 left them alone" where true (the 12 → 13 precedent of header-only edits) |

`V8.EntitiesSection` keeps `CreatureDto` (`Sections/SchemaV8.cs:46`), which schema 14 does not change.

### 4.3 The step

```csharp
/// <summary>
/// Schema 13 to 14 (M7): the player gains a faction ledger and each companion a route; the world gains placed pieces, the
/// structure sequence and NPC errands. Nothing before M7 was witnessed or told, built, or sent to work, and no one walked a
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

Rules this step obeys:
- **Purity.** No filesystem access, no definition resolution (`Migrations.cs:49-53`). A quarantined (null) section passes through as null.
- **Nullables pass through.** A v13 player with no companions list, or an entities section with no containers list, stays null and is refused by decode ("required from 12 / 6"). The step never repairs corruption.
- **The route default is written inline**, not through a factory on the current DTO. When schema 15 freezes `NavRouteDto`, this step then needs only a type rename.
- **Pre-M7 acts are not reconstructed** from flags or corpses (C §14.4). That would be a guess, and migrations never consult content.
- **Registration.** Append `new SchemaV13ToV14()` to `SchemaMigrations.Production` (`Migrations.cs:69-81`), add `using V13 = UNNAMED.Persistence.Sections.V13;`, and set `SaveFormat.SchemaVersion` to 14 (`SaveModel.cs:20`).

### 4.4 New DTOs (current shapes, in `SectionCodec.cs`)

All carry `[MessagePackObject]` with snake_case `[Key]`:
- `FactionsDto`, `ActDto`, `KnowledgeDto`, `StandingDto` (C §14.3);
- `NavRouteDto` (A §13.4);
- `PieceDto`, `NpcErrandDto` (B §9.2, with `route` required per D19).

New nullable members, each with the doc comment "Required from schema 14. The 13 -> 14 step gives older saves …":
- `PlayerDto.Factions`;
- `CompanionDto.Route`;
- `EntitiesSectionDto.Pieces`, `StructureSeq`, `NpcErrands`.

---

## 5. (e) Digests and canonical renderings

### 5.1 `PlayerRecord.Digest` → `unnamed.player/v10` (`PlayerState.cs:323-368`)

Every v9 term keeps its order. Two insertions:
- **Inside each companion, after its trail marks (`:362-363`):** `status key, goal x, goal z, planned tick, stamp (ulong), watch min x, min z, max x, max z, partial (bool), corner count, then each corner x, z`. `CanonicalHasher` has the `ulong` and `bool` overloads (`src/Domain/CanonicalHasher.cs:35`, `:39`).
- **After posture (`:365`):**
  - `NextActSeq`;
  - `acts count`, then each act `(seq, kind, subject, cell key, x, z, tick)`;
  - `knowledge count`, then each row `(knower, act, identity, source, via ?? "-", tick, delta)`;
  - `standing count`, then each row `(faction, points)`.

### 5.2 `WorldDelta.EffectiveCellDigest` → `unnamed.effective-cell/v2` (`WorldDelta.cs:582-635`)

The v1 terms are unchanged (tag at `:587`). After the population loop (`:625-633`), before `Finish`:
- `PiecesIn(cell).Count`, then each piece in ID order: `id, def, x, z, rotation, owner, health, door_open`;
- `NpcErrandsIn(cell).Count`, then each errand in NpcId order: `npc, phase key, piece ?? "-", owner ?? "-", x, z, facing, the route's §5.1 terms, stuck`.

Counts are hashed even when zero. A straddling piece is hashed once, in its anchor's host cell. That is exact, because `StateDigest` covers every region cell (`Simulation.cs:361-362`).

### 5.3 `Simulation.StateDigest` → `unnamed.simulation/v2` (`Simulation.cs:357-364`)

`tag, WorldTick, CaptureRecord().Digest, World.StructureSequence, cell count, cells…`. `CaptureRecord` (`:342-351`) gains `Factions = _state.Factions` beside `Posture`.

### 5.4 Nothing persisted moves

- No digest is written to a save. The load proof in `rotation.json` is the integrity-root digest (`src/Persistence/SaveStore.cs:218`, `:550-553`).
- `RegionDigest`, the pinned probe digest, hashes baselines only (`src/World/Generation.cs:322-332`).
- No test pins a digest value: grep finds the three tags only at their definitions.
- So the bumps change no fixture byte and no probe pin.

### 5.5 `CanonicalState.Render` (`tests/Persistence.Tests/CanonicalState.cs`)

Hand-written, so every new field is written explicitly. Enum values use their save keys; ulongs use `WorldSeed.Format` (`src/World/Determinism.cs:11`), as the appearance seed does (`CanonicalState.cs:37`).

- **Inside each companion, after `trail_mm` (`:139-145`):** a `route` object with `status`, `goal_mm` [x, z], `corners_mm` (flat), `planned_tick`, `stamp` (`"0x…"`), `watch_mm` [4], `partial`.
- **After `posture` (`:149-153`):** a `factions` object:
  - `next_act_seq`;
  - `acts` [{`seq`, `kind`, `subject`, `cell_key`, `x_mm`, `z_mm`, `tick`}];
  - `knowledge` [{`knower`, `act`, `identity`, `source`, `via` (or null), `tick`, `delta`}];
  - `standing` [{`faction_id`, `points`}].
- **After `creatures` (`:238-265`):**
  - `structure_seq`;
  - `pieces` [{`instance_id`, `def_id`, `host_cell`, `baseline_hash`, `x_mm`, `z_mm`, `rotation`, `owner`, `health`, `door_open`}];
  - `npc_errands` [{`npc_id`, `host_cell`, `baseline_hash`, `phase`, `piece_id` (or null), `work_owner` (or null), `x_mm`, `z_mm`, `facing_mdeg`, `route` {as above}, `stuck_ticks`}].

### 5.6 `StateDump` (`src/Application/StateDump.cs`)

The raw dump needs no change. The three `DeltaSnapshot` properties, `PlayerRecord.Factions` and `CompanionRecord.Route` are public properties of the two serialised roots (`:32-33`).

**One fix is needed for the replayable dump.** A piece chest's container key is `container.pce_<ulid lower-case>` (B §14.1). The piece ID it embeds is salted with the owner's `chr_`, and that ID is fresh on every new game (`src/Application/GameSession.cs:139`). The masking regex `^[a-z]{3}_[0-9A-Z]{26}$` (`StateDump.cs:93-94`) does not match that key. So two fresh runs that place a chest would differ in `Containers[*].Key`, and any `replayable: true` comparison would fail. Add:
- a second pattern `^container\.(pce_[0-9a-z]{26})$`;
- in `MaskIds`, the value becomes `"container.#"`;
- in `Name`, it becomes `"container." + Named(<the canonical pce_ ID: prefix lower-case, ULID upper-case, EntityId.Parse(...).Value>)`, so the key and its piece share one name (for example `container.pce#3`).

Test: `TwoFreshRuns_WithAPieceChest_ProduceTheSameReplayableDump`.

Known limit, recorded not fixed: `Order` sorts every array of objects by content (`:108-117`). Route corners and trail marks are therefore order-checked only by the raw dump and the digests, never by the replayable dump.

---

## 6. (f) The load pipeline: decode, pass, proof, rebase

### 6.1 Codec (`SectionCodec.cs`)

- **`EncodePlayer` (`:327-386`)** writes `Factions` from `player.Factions`, and `Route` on each companion.
- **`DecodePlayer` (`:396-432`):**
  - `dto.Factions ?? throw new FormatException("player.msgpack has no factions (required from schema 14)")`;
  - enum keys are parsed (`creature_killed|switch_set`, `witnessed|reported`, `unidentified|identified`), else `FormatException` naming the key;
  - the ledger is built as `FactionLedger`, and `PlayerRecord`'s init validation rejects bad order and ranges with `ArgumentException`. `SaveLoader` maps both exception types to `SaveCorruptionException` (`SaveLoader.cs:152-155`).
- **`Companion(CompanionDto)` (`:434-448`):**
  - `c.Route ?? throw new FormatException($"companion {c.NpcId} has no route (required from schema 14)")`;
  - then a shared `Route(NavRouteDto, string what)` helper checks: the status key; `goal_mm` of length 2; `watch_mm` of length 4; `corners_mm` of even length ≤ 64; and `NavRoute.Problem()` null. Any failure throws `FormatException`.
  - Companion routes are validated fully at decode because the player section is all-or-nothing (the posture precedent, `:410-416`).
- **`EncodeEntities` (`:478-549`):**
  - the `Prove` loop adds `foreach piece: Prove(p.HostCell, p.BaselineHash, p.InstanceId)` and `foreach errand: Prove(e.HostCell, e.BaselineHash, <an errand identity>)`. `Prove`'s third parameter becomes a `string` label, so that an errand, which has no `EntityId`, can be named;
  - it writes `Pieces`, `StructureSeq = snapshot.StructureSequence` and `NpcErrands`.
  - Without the `Prove` additions, a cell hosting only a piece would have no `baselines` entry. Decode would then stamp a null hash (`:559` lookup pattern) and `FromSnapshot` would reject every such piece.
- **`DecodeEntitySection` (`:555-596`)** becomes `public static DeltaSnapshot DecodeEntitySection(byte[] bytes)`. It returns `new DeltaSnapshot(ImmutableArray<CellDeltaRecord>.Empty, entities) { Created, Containers, Creatures, Pieces, StructureSequence, NpcErrands }`:
  - `pieces`, `structure_seq` and `npc_errands` null → `FormatException("entities.msgpack has no <key> (required from schema 14)")`;
  - `structure_seq < 0` → `FormatException`;
  - an unknown phase key → `FormatException`;
  - an errand `route` null → `FormatException`; its array lengths are checked as above; `Problem()` is checked at apply (§6.4);
  - each piece and errand gets `BaselineHash = baselines.GetValueOrDefault(HostCell)`.

  `DecodeEntities` (`:598`) becomes `DecodeEntitySection(bytes).Entities`. The three test deconstructions (`tests/Persistence.Tests/MigrationTests.cs:148`, `:179`, `:197`) switch to property access.

### 6.2 The four `DeltaSnapshot` construction sites (D15), and why each matters

| Site | Today | After | Failure if missed |
|---|---|---|---|
| `SaveLoader.cs:143-146` (decode) | 4-tuple → `new DeltaSnapshot(cells, entities) { … }` | `DecodeOrQuarantine(…, DeltaSnapshot.Empty) with { Cells = cells }` | every load loses the three properties |
| `SaveLoader.cs:356-358` (definition pass) | `new DeltaSnapshot(cells, entities…) { Created, Containers, Creatures }` | `delta with { Cells, Entities, Created, Containers, Creatures, Pieces, NpcErrands }` | on **every content update** (`content_hash` differs, `:158-159`), `StructureSequence` resets to 0. `TryApplyPiece`'s "ID timestamp ≤ `StructureSequence`" (B §9.3) then rejects every piece but none. If that check were absent, the next placement would mint `Derived(Piece, 1, …)` again and `CreateEntity` would throw on the registered or tombstoned ID (`src/EntityRegistry/EntityRegistry.cs:48-51`) |
| `BaselineTransitions.cs:112-117` (rebase) | `new DeltaSnapshot(…) { … }` | `delta with { Cells, Entities, Created, Containers, Creatures, Pieces, NpcErrands }` | as above, whenever a transition applies (every M6 save, §8.3) |
| `WorldDelta.cs:520` (`TakeSnapshot`) | `new DeltaSnapshot(…) { … }` | adds `Pieces`, `StructureSequence = _structureSequence`, `NpcErrands` | nothing saved |

**Guard G11** (D §4), as built here: `EveryDeltaSnapshotProperty_SurvivesDecodeTheDefinitionPassAndTheRebase`.
- It builds the v14 fixture snapshot and asserts, by reflection over `DeltaSnapshot`'s public properties, that **no property is empty or default in the input**. The test fails with "extend the builder" when a later property is added.
- It then encodes and decodes, runs the pass with identity aliases (a content identity whose hash differs, so the pass really runs), runs `SemanticRebase.Apply` over every host cell with a transition mapping the fixture generator to itself, and asserts every property equal element-wise.

### 6.3 `ProveBaselines` (`SaveLoader.cs:361-408`)

Add after the creatures loop (`:388-389`):

```csharp
foreach (var record in delta.Pieces)     Check(record.HostCell, record.BaselineHash);
foreach (var record in delta.NpcErrands) Check(record.HostCell, record.BaselineHash);
```

A piece is proven against its anchor's cell only. A straddling piece's other cells hold nothing of it; its footprint is content.

### 6.4 `WorldDelta.FromSnapshot` and the two `TryApply`s

The world-side checks are B §9.3's list. Where each check lives:
- **`TryApplyPiece`** (Section `"entities"`, key = instance ID):
  - host cell parse and hash match; kind `Piece`; `DefinitionId.IsValid(DefId)`; owner kind `Character`;
  - rotation 0..3; health ≥ 1;
  - `HostCell == CellKey.OfWorld(XMm/1000.0, ZMm/1000.0).ToString()`;
  - `InstanceId.Timestamp ≤ StructureSequence` (`src/Domain/EntityId.cs:46`);
  - not seen, not registered; then `CreateEntity(DefinitionId.Parse(DefId), InstanceId)`.
  - It does **not** check that the ID equals `Derived(…, Owner)`. That keeps a later ownership transfer a row edit, not a re-key.
- **`TryApplyNpcErrand`** (key = `npc_id`):
  - host parse and hash; `npc.*` shape; phase invariants (`to_work`/`at_work` carry both `piece_id` and `work_owner`; `to_home` has no `piece_id`);
  - facing in [0, 360 000); stuck ≥ 0; `Route.Problem()` null;
  - a named piece is present in `_pieces`, of kind `Piece`; at most one `to_work`/`at_work` per piece (a later duplicate by NpcId is rejected).
  - It registers nothing: NPC identities are derived and registered by `NpcSystem.Populate` (`Social.cs:127`).
- **`TryApplyContainer`** gains B's clause. A `container.pce_*` key needs its piece present ("its chest is gone") and the derived `cnt_` ("not its chest's identity").

### 6.5 `SemanticRebase.Apply` (`BaselineTransitions.cs:94-117`)

Pieces and errands move "as they are", re-stamped when their host cell is rebased, exactly like created instances (`:97-99`):

```csharp
var rebasedPieces  = delta.Pieces.Select(p => cellsToRebase.Contains(p.HostCell) ? p with { BaselineHash = baseline(CellKey.Parse(p.HostCell)).Digest } : p).ToImmutableArray();
var rebasedErrands = delta.NpcErrands.Select(e => cellsToRebase.Contains(e.HostCell) ? e with { BaselineHash = baseline(CellKey.Parse(e.HostCell)).Digest } : e).ToImmutableArray();
return delta with { Cells = …, Entities = …, Created = …, Containers = …, Creatures = …, Pieces = rebasedPieces, NpcErrands = rebasedErrands };
```

Legality after a rebase is not checked here. Terrain is not in the baseline, and B §9.6's `StructureAudit` re-checks each piece against the running layout, keeping and reporting any that no longer fit.

### 6.6 The `PlayerRecord` copy trap, and the companion copy sites

The pass ends `player.WithInventory(…)…WithCompanions(…)` (`SaveLoader.cs:356-357`). Every `With*` constructs a new record and hand-lists the init properties (`PlayerState.cs:222-253`, today `{ Posture = Posture }`). **All seven must become `{ Posture = Posture, Factions = Factions }`.** A single omission silently resets the ledger on every content update.

Guard (new): `EveryPlayerRecordInitProperty_SurvivesEveryWithMethod`, by reflection. It applies each `With*` to the v14 fixture player with an identity argument and asserts every public property equal.

Companion routes have the same trap at two runtime copy sites:
- `CompanionSystem.Populate` (`src/World/Runtime/Companions.cs:135-141`);
- `CompanionSystem.Records` (`:149-155`).

Both must carry `Route`. The test `ACompanionRoute_SurvivesPopulateAndCapture` boots a `Simulation` with a companion that holds an active route and asserts `CaptureRecord().Companions[0].Route` is equal.

---

## 7. (g) The definition-ID pass (`SaveLoader.ResolveDefinitions`)

It runs only when `content_hash` differs (`SaveLoader.cs:158`). Every rewritten list is rebuilt through an ordinal `SortedDictionary` or `SortedSet`, so canonical order survives a rename. `faction.fixture.delvers` → `diggers`, for example, can move a row.

| Stored ID | Where | Renamed / Replaced | Discarded | Two resolve to one |
|---|---|---|---|---|
| `PieceRecord.DefId` | `delta.Pieces` | rewritten. A family change is the audit's to report (B §9.6) | the piece is dropped. `Loss`: "piece {id} in cell {host}: '{def}' was removed with no replacement; dropped". **A storage piece also spills** (below) | n/a: pieces are keyed by instance ID |
| `NpcErrandRecord.NpcId` | `delta.NpcErrands` | rewritten | the errand is dropped. `Loss` via `Resolve` ("npc errand {npc}") | keep the first by original `NpcId` (the companion precedent, `:348-354`); the other is added to `Loss`: "npc errand {old}: merged into {new}'s; its pose is dropped" |
| `FactionStanding.FactionId` | `player.Factions.Standing` | rewritten | the row is dropped. `Loss`: "player faction standing '{id}' ({points}) was removed with no replacement; dropped" | points summed, then clamped to [-999, 1000] (C §14.5). The constants live in Domain as `FactionLedger.OrdinaryFloor = -999` and `MaxPoints = 1000`; FAC001 pins `config.factions` to them |
| `FactionKnowledge.Knower` | `.Knowledge` | rewritten | the row is dropped, under the same faction's `Loss` line | union by (`knower`, `act`): identified beats unidentified, then the lower tick |
| `ActRecord.Subject` | `.Acts` | rewritten | the act and its knowledge rows are dropped; standing is kept; `Loss`: "player act {seq}: '{subject}' was removed with no replacement; dropped". `NextActSeq` never decreases | n/a: acts are distinct instances |
| `FactionKnowledge.Via` | `.Knowledge` | rewritten | **set to null, and the row is kept, with a `Warning`, not `Loss`**: "knowledge of act {seq} by {knower}: its witness '{npc}' was removed; the knowledge stays". Call `content.Resolve` directly, not the local `Resolve`, whose Discarded branch always writes `Loss` "dropped" (`:222-225`) | rewritten |
| `container.pce_*` keys | `delta.Containers` | **never resolved**: a layout-style key, like authored container keys today (`:271-281` resolves only items) | - | - |
| Instance IDs (`pce_`, `cnt_`, `chr_` owners, `WorkOwner`, `PieceId`) | - | not definitions: untouched | - | - |
| `NavRoute` (all fields) | - | no IDs: untouched. `Stamp` is a content-dependent cache key; a mismatch replans (A §7.6) | - | - |

**The spill rule for a dropped storage piece.** Without it, `TryApplyContainer` would reject the orphaned chest ("its chest is gone"), and its items would be lost behind a rejected-record warning. The pass instead:
1. removes the chest's `ContainerRecord` (key `"container." + id.Value.ToLowerInvariant()`);
2. appends one `CreatedEntityRecord` per item, which keeps the `itm_` ID, the resolved definition, count and quality. Each record takes:
   - the piece's `HostCell` and `BaselineHash` (the anchor's cell, which `ProveBaselines` then checks);
   - cell-relative position `((int)WorldMath.FloorMod(x_mm / 10, CellSizeCm), (int)WorldMath.FloorMod(z_mm / 10, CellSizeCm))`, the fixed-node pattern (`GameSession.cs:105-106`);
3. writes `Warning`: "{n} stacks from the chest of piece {id} were put on the ground".

This mirrors destroy's spill (B §10.3) and keeps player property. Dependents of a dropped piece (a door on a dropped doorway, walls on a dropped pad, an errand at a dropped station) are left to B §9.6's audit, which keeps and reports them.

Ends: `player.With…(…) with { Factions = resolvedLedger }` and `delta with { … }` (§6.2).

Report wording uses the existing `Resolve(id, referencedBy)` role strings (`:209-232`):
- `$"piece {p.InstanceId} in cell {p.HostCell}"`;
- `$"npc errand {e.NpcId}"`;
- `"player faction standing"`;
- `$"player faction knowledge of act {k.Act}"`;
- `$"player act {a.Seq}"`.

---

## 8. (h) Content packs, the probe mirror, and the baseline transition

### 8.1 Writer pack `tests/Persistence.Tests/Fixtures/content-0.1.7/`

A full copy of `content-0.1.6/` plus the definitions the v14 records name. Following D §1.5, it also includes the three configs, so the pack is one the v14 writer could have run with. Writer packs need not pass today's checks (`Fixtures/README.md:37-38`); only their hash matters.

| File | ID |
|---|---|
| `pieces/fixture/pad.yaml`, `old_wall.yaml`, `doorway.yaml`, `door.yaml`, `chest.yaml` | `piece.fixture.pad`, `piece.fixture.old_wall`, `piece.fixture.doorway`, `piece.fixture.door`, `piece.fixture.chest` (B §20's shapes and numbers, costing timber) |
| `factions/fixture/keepers.yaml`, `delvers.yaml` | `faction.fixture.keepers` (reaction `creature_killed wolf_grey +100`), `faction.fixture.delvers` (`creature_killed wolf_grey -100`, `switch_set world.lever.mill_gate +100`); seat `location.wolf_den` (the 0.1.x name) |
| `npcs/fixture/smith.yaml` | `npc.fixture.smith` (`faction_ref: faction.fixture.delvers`) |
| `items/material/timber.yaml` | `item.material.timber` |
| `config/building.yaml`, `factions.yaml`, `navigation.yaml` | `config.building`, `config.factions`, `config.navigation` (the game's M7 values) |

The act 2 subject is the existing `world.lever.mill_gate` (`content-0.1.6/world_flags/lever/mill_gate.yaml`), so no new flag is needed. `M2Fixtures.Historical.WriterContentVersion` becomes `"0.1.7"` and `WriterContentHash` the computed hash (`tests/M2.Probe/M2Fixtures.cs:109-110`). `MigrationTests.cs:754` loads `"content-0.1.7"`.

### 8.2 Current fixture pack `Fixtures/content`, 0.2.8 → 0.2.9

- **Adds:**
  - `piece.fixture.pad`, `piece.fixture.wall` (the renamed wall), `piece.fixture.doorway`, `piece.fixture.door`, `piece.fixture.chest`;
  - `faction.fixture.keepers`, `faction.fixture.diggers` (renamed), each seated at `location.den_mouth`;
  - `npc.fixture.smith` (`faction_ref: faction.fixture.diggers`, no `companion:` block, so FAC-M2 holds);
  - `item.material.timber`; `config.building`, `config.factions`, `config.navigation`.
- **`_aliases.yaml` gains** `piece.fixture.old_wall: piece.fixture.wall` and `faction.fixture.delvers: faction.fixture.diggers`, and its header comment gains "0.2.9 added the M7 definitions and renamed the old wall and the delvers".
- **It must pass today's checks, including the new validators.** `Fixtures.Content()` throws "The fixture content does not validate" otherwise (`tests/Persistence.Tests/HistoricalFixtureTests.cs:28-30`, via `SaveToolApp.ReadContent`, `src/SaveTool/SaveToolApp.cs:98-110`). Specifically:
  - BLD006 needs `config.building` whenever a piece exists (B §19.2);
  - FAC001 needs `config.factions` whenever a faction exists, and FAC-M1 refuses an unplaced member (C §16.6). So `regions/fixture_vale.yaml` gains `npcs: [{ npc_ref: npc.fixture.smith, position_m: [32, 70], facing_deg: 0 }]`: 2 m from `location.den_mouth`'s anchor (30, 70), inside its 8 m radius and the region's 100 m bounds (`Fixtures/content/locations/den_mouth.yaml`, `regions/fixture_vale.yaml`);
  - NAV004/NAV005 tie `config.navigation` to `config.base_speeds` (A §13.3). The fixture copy has `body_radius_m: 0.35` but no `stand_height_m` (`Fixtures/content/config/base_speeds.yaml`; the key is optional, `src/Content/WorldContent.cs:71-75`), so NAV005 must compare against the built `MovementRules` value, which is the default when the key is absent, not against the raw key;
  - if a lint rejects the pack, fix the pack's content, never the lint.
- **Version constants:**
  - `Fixtures.ContentVersion = "0.2.9"` (`HistoricalFixtureTests.cs:16`);
  - `M2Fixtures.Historical.CurrentContentVersion = "0.2.9"`, and `CurrentContentHash` the computed hash (`M2Fixtures.cs:252-253`);
  - the `CurrentContent()` mirror (`:259-283`) gains the 12 new IDs in ordinal order, and both aliases. `TheProbesContentMirror_IsTheFixtureContentPack` (`MigrationTests.cs:735-745`) pins it.
- **README policy 4** (`Fixtures/README.md:25-38`) gains the sentence: "`content-0.1.7/` - 0.1.6 plus the pieces, factions, NPC, material and configs the schema-14 records name - is the pack v14 was written with; 0.2.9 added the M7 definitions and renamed the old wall and the delvers (M7)".

### 8.3 The game's baseline transition, and the frozen M6 fingerprint

**Why it is needed.**
- Fixed nodes are generator input (`src/Application/GameSession.cs:104-107`), and `CellBaseline.Digest` covers nodes (`src/World/Generation.cs:144-162`).
- The deadfall at (84, 118) (B §8.1) therefore changes cell A's (`r_0_0:c_00_01`) baseline hash and the running fingerprint.
- Cell A holds both authored doors (`content/regions/ashen_hollow.yaml:143-144`) and the waystation chest (`:148`), so almost every M6 save has a changed cell A.
- Without a transition, every such save refuses at `ProveBaselines` (`SaveLoader.cs:397-405`).
- The other M7 content (build area, factions, navigation and building config, NPC fields, pieces) is layout or content, not generator input, and moves no baseline.

**The procedure (performed once, in slice S2 before the deadfall lands; recorded in `docs/M7_STATUS.md`):**
1. In a scratch worktree of `e10d2c4` (the precedent for writing v1 in a worktree of `7ff4c57`, `Fixtures/README.md:83`), write the real M6 save of §12.2 with that build. Its `manifest.json` `worldgen_fingerprint` is the running M6 fingerprint.
2. In `GameSession.cs`, beside `M3LayoutFingerprint` (`:76-77`), add:

   ```csharp
   /// <summary>The generator fingerprint of M6's layout, the one M6 (schema 12-13) saves were written against. Frozen: it names a past layout.</summary>
   private const string M6LayoutFingerprint = "sha256:<the value from step 1>";
   ```
3. After the M6 transition (`:115-117`):

   ```csharp
   // A save from M6's layout carries onto M7's, which adds the deadfall by the crossing to cell A: nothing it holds was that node.
   if (generator.Fingerprint != M6LayoutFingerprint)
       transitions = transitions.Add(new BaselineTransition("M7: the deadfall by the crossing", M6LayoutFingerprint, generator.Fingerprint));
   ```

   `DropVanishedTargets` stays false. Adding a node vanishes nothing, so any vanished target is a real error. The M3f and M6 transitions keep targeting the running fingerprint, so an M3-layout save still loads in one hop (there is no chaining, `SaveLoader.cs:395-396`).
4. Test `TheM6LayoutFingerprint_IsTodaysLayoutWithoutTheDeadfall`. It builds a generator from the running layout's fixed nodes minus `deadfall`, with the same terrain rule (the `CraftingTests.cs:444-460` construction), and asserts its fingerprint equals the constant. This pins the constant against a typo, **and fails if M7 changed any other generator input**, which would then need its own transition review.
5. Test `AnM6LayoutSave_LoadsOntoM7s_ThroughTheDeadfallTransition` (the pattern of `CraftingTests.cs:409-431`):
   - write a save under the M6-layout generator with the longhouse door opened and an item put in the waystation chest;
   - loading with a bare `LoadContext` refuses and names `r_0_0:c_00_01` in `CellsMismatched`;
   - `session.Load` succeeds with `CellsRebased` containing `"r_0_0:c_00_01 (M7: the deadfall by the crossing)"`, empty `Loss`, the door flag and chest contents carried, and the deadfall `Ready`.

**Recorded limit.** `SaveTool` builds its `LoadContext` with no transitions (`SaveToolApp.cs:62`). `save:migrate --dry-run` on a real M6 Ashen Hollow save therefore refuses cell A, as it already does for M3-layout saves (`research/persistence.md` §6.2). M7 does not change `SaveTool`; `M7_STATUS` records the limit.

The fixture world uses its own profile (`M2Fixtures.Profile()`, pinned by `MigrationTests.cs:743`), which M7 does not touch. The fixtures need no transition, and v13 and v14 share one fingerprint.

---

## 9. (i) Fixtures

### 9.1 The v14 fixture world (`M2.Probe fixture <dir>`, then copy `<dir>/quick` to `Fixtures/v14/quick`)

`M2Fixtures.Historical.Player()` and `World(Registry)` gain the following. M2.Probe reaches World's internal mutators (`src/World/World.csproj:21`). IDs are deterministic: `PlayerId` is fixed (`M2Fixtures.cs:61-62`), pieces use `EntityId.Derived`, and the foreign owner is `EntityId.Create(Character, 1_700_000_000_200, [5,5,5,5,5,5,5,5,5,5])`.

**Player.**

| Field | v14 value (writer IDs; names after load in brackets) | Proves |
|---|---|---|
| warden companion `route` | active; goal (150 250, -40 125); corners (149 250, -41 000), (149 750, -40 750), (150 250, -40 125); planned tick 4 990; stamp `0x0123456789ABCDEF`; watch (129 000, -61 000, 171 000, -20 000); **partial true** | every route field non-default, in the player section |
| `factions.next_act_seq` | 3 | the sequence survives |
| act 1 | `creature_killed`, `creature.beast.wolf_grey`, `r_0_0:c_00_02`, (20 000, 250 000), tick 4 100 | an act with a creature subject |
| act 2 | `switch_set`, `world.lever.mill_gate`, `r_0_0:c_00_07`, (50 000, 750 000), tick 4 200 | an act with a flag subject |
| knowledge | (`delvers` [`diggers`], 1, identified, reported, via `npc.fixture.smith`, 4 300, -100); (`delvers` [`diggers`], 2, unidentified, witnessed, via `npc.fixture.smith`, 4 200, 0); (`keepers`, 1, identified, witnessed, via `npc.fixture.warden` [`warden_sera`], 4 100, +100) | the **same act moving two factions in opposite directions**, in stored form; a rename reaching `knower` and `via` |
| standing | (`delvers` [`diggers`], -100), (`keepers`, 100) | a rename reaching standing |

**World.** Pieces sit around the z = 500 m seam, which lattice squares straddle because 500 is not a multiple of 3 (B §0.4). All are owned by Aelin unless stated.

| seq | def (writer) | anchor (mm) | r | host cell | health | door_open | notes |
|---|---|---|---|---|---|---|---|
| 1 | `piece.fixture.pad` | (49 500, 499 500) | 0 | `c_00_04` | 200 | false | the square z 498-501 **straddles the c_00_04 / c_00_05 seam** |
| 2 | `piece.fixture.doorway` | (49 500, 498 000) | 0 | `c_00_04` | 200 | false | |
| 3 | `piece.fixture.door` | (49 500, 498 000) | 0 | `c_00_04` | 120 | **true** | |
| 5 | `piece.fixture.old_wall` [`piece.fixture.wall`] | (49 500, 501 000) | 0 | **`c_00_05`** | **150** | false | a structure hosted in two cells; damaged; renamed on load |
| 7 | `piece.fixture.chest` | (49 500, 499 500) | 0 | `c_00_04` | 100 | false | its container site (49 500, 500 400) is in **`c_00_05`** |
| 9 | `piece.fixture.pad` | (61 500, 499 500) | **1** | `c_00_04` | 200 | false | **foreign owner** |
| `structure_seq` | 9 | | | | | | gaps at 4, 6 and 8: a sequence is not a count |

- **Chest container:** key `"container." + chest7.Value.ToLowerInvariant()`, `Derived(Container, 7, "unnamed.piece-container/v1", chest7.Value)`, host `r_0_0:c_00_05`, one stack of `item.material.timber` ×3.
- **Errand:** `npc.fixture.smith`, host `r_0_0:c_00_07`, `to_home`, no piece, work owner Aelin, pose (50 000, 520 000) at 180 000 mdeg, stuck 3. Route active: goal (50 000, 750 000); corners (50 000, 600 000), (50 000, 750 000); planned tick 4 980; stamp `0xFEDCBA9876543210`; watch (30 000, 500 000, 70 000, 770 000); partial false. This is the route DTO inside `entities.msgpack`.

Every record lies in the ten existing cells (`M2Fixtures.cs:37-38`), so `CellsMatched` stays 10 (`HistoricalFixtureTests.cs:288`). The fixture is written at world tick 5 000, like every other.

### 9.2 `expected.json`

1. Write `v14/expected.json` and regenerate `v1..v13/expected.json` in one run: `UNNAMED_WRITE_FIXTURE_EXPECTATIONS=1 dotnet test tests/Persistence.Tests --filter "FullyQualifiedName~Fixture_LoadsToItsExpectedCurrentState"` (`HistoricalFixtureTests.cs:107-108`).
2. Review every diff line by line. Each older file gains **exactly**:
   - `factions` with `next_act_seq: 1` and three empty arrays, after `posture` (6 lines);
   - `structure_seq: 0`, `pieces: []`, `npc_errands: []` after `creatures` (3 lines);
   - for v12 and v13 only, a `route` object with status `none` and zero fields after the warden's `trail_mm`.
   
   Nothing else may change: not a value, not an order, not a key. A diff with anything more is a migration bug, and the fix is never to the expectation (`Fixtures/README.md:20-22`).

**Must not change:**
- the bytes of `v1..v13/quick/**`;
- `content-0.1.0 … 0.1.6`;
- `worldgen_profile.json` and `M2Fixtures.Profile()`;
- the wire shape of every frozen type (`SchemaV12.cs`'s edit is a type rename with identical keys);
- the older schemas' alias expectations (`:248-287`);
- `CellsMatched`; the empty `Loss`;
- the manifest identity of older fixtures;
- `.gitattributes`' fixture rule (line 3).

### 9.3 Test edits the bump forces

- **`HistoricalFixtureTests`:**
  - `>= 12` becomes `12 or 13`;
  - a new `>= 14` alias list: the ≥ 12 list with `npc.fixture.warden -> npc.fixture.warden_sera x4` (2 relationships, 1 companion, 1 via), plus `faction.fixture.delvers -> faction.fixture.diggers x3` and `piece.fixture.old_wall -> piece.fixture.wall`, in the report's sorted order (`src/Persistence/MigrationReport.cs:57`, `:148`);
  - an M7 block of independent assertions (§10.2).
- **`MigrationTests`:**
  - `:78` 11 → 12, plus `Steps[11]` "schema 13 -> 14:";
  - the prefix lists at `:104`, `:124`, `:142`, `:162`, `:178`, `:194`, `:214`, `:230`, `:246` each gain `"schema 13 -> 1"`;
  - `:262` `Assert.Single` becomes the two-step list;
  - `:398` 12 → 13, plus `Steps[12]`;
  - `:428` "…to schema 14";
  - `:754` `content-0.1.7`;
  - `:148`, `:179`, `:197` property access (§6.1).
- **Unchanged and still valid:** `EveryShippedSchemaVersion_HasAFixture_AndNoneFromTheFuture` (`:81-96`), `TheProductionChain_HasOneStepPerVersion_InOrder` (`MigrationTests.cs:415`), and the kill-mid-migration test, which now exercises v14.
- **`Fixtures/README.md`:**
  - the world table gains the rows of §9.1, one per proof;
  - the provenance table gains `v14/ | The M7 schema-14 writer | M2.Probe fixture <dir> (writes content identity 0.1.7)`;
  - policy 4 is as in §8.2.

---

## 10. (j) Field-by-field acceptance evidence

### 10.1 How the StateDump comparison grows

`Compare` counts one leaf per scalar, including nulls, one per missing key, and one per array-length mismatch. Empty arrays and objects add nothing (`StateDump.cs:57-89`). The 415 of M6 is the size of that run's state, not a constant (`docs/M6_STATUS.md:172`). Per-record leaf costs, from the record shapes:

| Added | Leaves |
|---|---|
| `world.StructureSequence` | 1, always |
| `player.Factions` | 1 (`NextActSeq`) + 7 per act + 7 per knowledge row + 2 per standing row |
| a `NavRoute` (companion or errand) | 10 + 2 per corner (`Status`, `GoalXMm`, `GoalZMm`, `PlannedTick`, `Stamp`, `Partial`, 4 `Watch` values) |
| a piece | 10 (`InstanceId`, `DefId`, `HostCell`, `XMm`, `ZMm`, `Rotation`, `Owner`, `HealthCurrent`, `BaselineHash`, `DoorOpen`) |
| an errand | 10 + its route |
| a piece chest | 4 + 4 per stack (the existing container shape) |

**Estimates:**
- **The M6 `--playthrough-verify` run, extended with C §7.5's faction beats (D32):** 415 + 1 + 10 (Tavar's route, if `none` at the save; +2 per corner if active) + 38 (two acts, three knowledge rows, the Waystation's single standing row; the Survey ends at 0 and stores no row) ≈ **464**, plus the dialogue memory the new reply nodes add (about +3 to +9). Expect **about 470**. The criterion is **0 differences**, not the number. `M7_STATUS` records the measured count with this breakdown, and any gap from the estimate is explained, not rounded.
- **The existing `StateDumpTests.ASaveAndALoad_CompareEqual_FieldByField`** (a new game, no companion) grows by exactly 2 (`StructureSequence`, `NextActSeq`) and still asserts `> 100` (`tests/Application.Tests/StateDumpTests.cs:19-34`).
- **The Crossing Workshop at step 8** (B §11: 19 pieces, Kera `at_work`, a chest with timber, Tavar waiting): ≥ 190 + 20 + 8 + 10 leaves beyond a new game.

### 10.2 The comparison tests, exactly

| # | Test (project) | What it compares | Proves |
|---|---|---|---|
| E1 | `T01_M7State_RoundTripsEveryField_ByteStable` (Persistence, `RoundTripTests`) | the v14 fixture player and world through `SaveStore.Save`/`Load`: `PlayerRecord.Digest`, `M2Fixtures.WorldDigest` (`M2Fixtures.cs:298-304`, now over effective-cell v2), and a resave of `player.msgpack` and `entities.msgpack` byte-identical | T-01 and T-03 for every new field |
| E2 | `Fixture_LoadsToItsExpectedCurrentState(14)` + `Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState(1..14)` (`HistoricalFixtureTests.cs:98-316`) | `CanonicalState` against `expected.json`, **plus an M7 block asserting every new field by value independently of `expected.json`**: the six piece IDs equal `Derived(Piece, {1,2,3,5,7,9}, …)`; seq 5's `DefId == "piece.fixture.wall"` and health 150; seq 3's `DoorOpen`; seq 9's owner and rotation 1; the two host cells; `StructureSequence == 9`; the chest container's key, derived ID, host `c_00_05` and timber ×3; the smith's errand (phase, pose, stuck 3, a 2-corner route); the warden's 3-corner partial route; the ledger (`next 3`, diggers -100, keepers 100, three knowledge rows with `via` renamed to `npc.fixture.warden_sera`). For `schema < 14`: no pieces, seq 0, no errands, `FactionLedger.Empty`, and every companion route `NavRoute.None` | the migration default and the v14 values, without trusting the generated file (`CanonicalState` omissions are invisible to `expected.json`, which it generates) |
| E3 | `EveryDeltaSnapshotProperty_SurvivesDecodeTheDefinitionPassAndTheRebase` (Persistence) | §6.2 | D15 / G11 |
| E4 | `EveryPlayerRecordInitProperty_SurvivesEveryWithMethod` (World or Persistence) | §6.6 | the `With*` trap |
| E5 | `EveryPersistedField_MovesItsDigest` (World, D's G12) | for `PieceRecord`, `NpcErrandRecord`, `NavRoute` (via both hosts), `ActRecord`, `FactionKnowledge`, `FactionStanding`, `FactionLedger.NextActSeq` and `CompanionRecord.Route`, change each public property in turn and assert that `EffectiveCellDigest` or `PlayerRecord.Digest` changes; and `StructureSequence` moves `StateDigest` | digest completeness |
| E6 | `ASaveAndALoad_CompareEqual_FieldByField` (existing) and `ABuiltStaffedAndKnownWorld_CompareEqual_FieldByField` (Application, new) | the Crossing Workshop at B §11 step 8 plus the P1-P5 ledger: `StateDump.Compare` before save against after a fresh `GameSession` load. 0 differences; leaves ≥ the §10.1 floor | the whole M7 state through `GameSession` |
| E7 | `Building_RoundTripsThroughSave_AndNavigationDerivesIdentically` (B §9.7) | rows, seq, dump, **grid digest**, `Space.Blockers` element-wise | derived state rebuilt identically |
| E8 | save-then-continue equals continue: A's N-A6 (errand and companion mid-route, `StateDigest` every 50 ticks for 400 ticks) and C's `SaveThenContinue_EqualsContinue_WithFactions` | `StateDigest` | persisted mover state is complete |
| E9 | `--playthrough-verify` (Presentation) | the 415+ field compare of §10.1 | the acceptance evidence |
| E10 | `TwoFreshRuns_WithAPieceChest_ProduceTheSameReplayableDump` (Application) | replayable dumps of two fresh new games that build the same chest | §5.6's masking |

---

## 11. (k) Quarantine and corruption behaviour

The new lists live in existing files. Every existing rule applies unchanged, and the new ones follow from where the data lives.

| Damage | Behaviour | Evidence / test |
|---|---|---|
| `entities.msgpack` fails its hash, or cannot be decoded (a missing required list, an unknown phase key, a malformed route array) | the whole section is quarantined (`SaveLoader.cs:85-93`, `:410-430`). Pieces, `structure_seq`, errands, piece chests, other containers, creatures and created instances are all lost together. The report names the section, not each structure: "report every lost structure" (`docs/PERSISTENCE.md:136`) is impossible when the section is unreadable (B open issues). `IsComplete` is false (`SaveModel.cs:131-132`), so `SaveStore.Migrate` refuses (`SaveStore.cs:260-263`); an in-game load proceeds with the warning | `AQuarantinedEntitiesSection_LosesStructuresErrandsAndChestsTogether_AndSaysSo` |
| An **individual** piece or errand row invalid but decodable (rotation 4, health 0, host cell not its anchor's, ID timestamp > seq, errand at a missing piece, a duplicate worker, a bad route) | that row alone is rejected (`RejectedRecord("entities", key, reason)`) and the rest loads (§0 item 4). A chest whose piece was rejected is rejected in turn ("its chest is gone") | `AnInvalidPieceRow_IsRejectedAlone`, `AnInvalidErrandRow_IsRejectedAlone`, `APieceChestWithoutItsPiece_IsRejected`, `APieceChestWithTheWrongIdentity_IsRejected` |
| `structure_seq` quarantined with its section | the world restarts at 0 with no pieces. The next placement mints `Derived(Piece, 1, …)` into a registry that holds no piece, so there is no collision | covered by the quarantine test |
| `cells.msgpack` quarantined, entities fine | pieces, `door_open` and errands load: their proofs use the entities section's own `baselines` table (`SectionCodec.cs:559`). Authored door flags are lost, as today, and movers plan through doors in any state and open them (A §12) | `ACellsQuarantine_LeavesPiecesAndErrandsWhole` |
| `player.msgpack` corrupt, including a missing `factions` or companion `route` | fatal `SaveCorruptionException`, which offers backups and never loads them automatically (`SaveLoader.cs:81-84`, `:152-155`) | the corrupt-not-defaulted tests below |
| Cross-section references | none introduced. Errands, chests and doors reference pieces in the same section; the player section holds no instance ID into the world (routes hold none; the ledger holds definition IDs and cell keys) | by construction (B §9.1) |

"Corrupt, not defaulted" tests (the pattern at `MigrationTests.cs:270-278`, `:348-356`):
- `ASchema14PlayerWithoutFactions_IsCorrupt_NotDefaulted`; also an unknown `identity` and unsorted acts;
- `ASchema14CompanionWithoutARoute_IsCorrupt_NotDefaulted`; also status `"wandering"`, odd `corners_mm`, 33 corners, `watch_mm` of length 3;
- `ASchema14EntitiesSectionWithoutPiecesSeqOrErrands_IsCorrupt_NotDefaulted`, one case per key;
- `AnErrandWithoutARoute_IsCorrupt_NotDefaulted`.

---

## 12. (l) Historical-save behaviour

### 12.1 Persistence level (the fixture world)

`Schema13To14_GivesNothingBuiltNoLedgerAndNoRoutes`: migrate `v13` (`Fixtures.Copy(13)`), then decode.
- **Asserted empty:** `Pieces` empty, `StructureSequence` 0, `NpcErrands` empty, `Factions == FactionLedger.Empty`, and the warden's route `NavRoute.None`.
- **Asserted kept:** everything schema 13 held (the warden's trail, the crouched posture, the quests, the corpse container).
- **Asserted not reconstructed:** the ledger is empty although the fixture world holds a wolf corpse (`spawn.fixture.den#1`) and `world.lever.mill_gate = 3`, so pre-M7 acts are provably not reconstructed.

### 12.2 Game level (a real M6 save)

**The data.** `tests/Application.Tests/Saves/m6_hollow/quick/**`:
- written once by the e10d2c4 build, in the scratch worktree of §8.3 step 1, from a one-off headless test:
  - a new game with seed 42;
  - the `StateDumpTests.Played` walk (`StateDumpTests.cs:8-16`), which opens `door.longhouse`;
  - then `door.forge_shed` opened;
  - the starting `item.tool.water_flask` moved into `container.waystation_chest` (the item `CraftingTests.cs:420-421` moves);
  - a hand-built recruited Tavar (the `CompanionTests` construction, B §11), following;
  - `session.Save("quick")`.
- a `README.md` beside it holding the one-off snippet, the commit and the manifest's fingerprint;
- `.gitattributes` gains `tests/Application.Tests/Saves/*/quick/** binary`;
- the save is never edited. It lives outside `Fixtures/`, so the fixture-count test does not see it.

**The test.** `AnM6Save_LoadsIntoM7_WithNothingBuiltNeutralStandingNoErrandAndNoRoute`: copy the data into a `TempProfile`, then `session.Load("quick")`, and assert:
- **Migration report:**
  - `Report.Steps` is exactly `["schema 13 -> 14: …"]`;
  - `CellsRebased` names `r_0_0:c_00_01` with the M7 transition;
  - `Blockers` and `Loss` are empty;
  - the definition pass ran (the content hash differs) with no aliases.
- **The world:**
  - `Simulation.Pieces` is empty and `StructureRevision == 0`;
  - no errand exists and Kera stands at her site;
  - both door flags and the chest's contents are carried;
  - the deadfall is `Ready`.
- **Factions:**
  - every `FactionView` has 0 points, tier `neutral` and level 0, and `Acts` is empty;
  - Kera's billet row is withheld from `Simulation.Wares`;
  - Sel's `notes` reply is not offered.
- **Tavar:** a companion with `Route == NavRoute.None` and his trail kept.
- **Navigation and derived state:** `Simulation.Navigation`'s grid digest equals a new game's (no pieces).
- **The first save afterwards:** a `session.Save("quick")` leaves `pre_migration_13_quick` byte-identical to the committed data (`SaveStore.cs:296-300`), and a reload compares equal by `StateDump.Compare` (0 differences).

---

## 13. (m) Document updates the persistence slice makes

| Document | Change |
|---|---|
| `docs/PERSISTENCE.md` §2 (`:78-90`) | Replace the navmesh row (`:83`, "Baking / streaming pipeline") with §3.1's rows: rebuilt in the `Simulation` constructor from content plus piece rows (owner ruling 1) and on every footprint change, never saved; a mover's committed route **is** saved. Fix "§7.4 step g" → "step k" (`:86`) |
| §3.2 (`:106-121`) | Mark `companions`, `buildings`, `journal`, `command_log` and `orphans` **not built**; as built, format 1 has four sections, with M7's lists inside `entities.msgpack` and `player.msgpack`. A new section file needs a schema-aware integrity root first |
| §5.1 (`:186-200`) | Add "**Factions (schema 14)**" (the ledger: acts, knowledge and standing; tiers derived, never stored; the pass rules of §7) and "**Routes (schema 14)**" (each companion's committed route; search state is never saved). Replace the stale "Implemented so far (schema 7)" paragraph (`:200`) with the schema-14 list |
| §5.3 (`:221-247`) | Add pieces, `structure_seq` and `npc_errands`: absolute-mm anchors, host cells and proof; piece chests as schema-6 containers with derived keys and IDs |
| §5.4 (`:248-252`) | Rewrite as built: no structure record; `pce_` rows in `entities.msgpack`; retirement by dismantle or destroy; the quarantine names the section |
| §6.2 chain table (`:344-357`) | Add rows 11 → 12 (companions), 12 → 13 (posture) and 13 → 14 (this step's summary) |
| §6.4 | Add the M7 transition and the frozen-fingerprint procedure |
| §7.4 (`:499`) | As-implemented note: the `FromSnapshot` order and the constructor's rebuild order (§3.2 here) |
| §10 required tests | Name E1-E10 and the corrupt-not-defaulted set against their T-numbers (T-01, T-03, T-21's bounded event log) |
| `docs/DATA_MODEL.md` §6 (`:846-850`) | Correct rule 1 to the implemented doctrine (every added persisted field bumps the schema, required on decode). Save-sensitive surfaces: `config.building.module_m` (anchors are absolute); `EntityId.Derived` tags `unnamed.piece/v1` and `unnamed.piece-container/v1` (changing one re-keys every piece); the ladder is **not** save-locked (tiers derived); `config.navigation.node_m` is not save-locked (routes are in mm) |
| `tests/Persistence.Tests/Fixtures/README.md` | §9.3's rows, provenance and pack narrative |
| `docs/M7_STATUS.md` (persistence section) | the schema-14 paragraph in the M6 form (`docs/M6_STATUS.md:163`): fields, frozen shapes, the fixture, the expected-diff review, the digest tags, the M6 fingerprint value, the measured StateDump count with its breakdown, and the `SaveTool` limit |

---

## 14. Slice plan (D's S3 and S4) and the files touched

**S3 (world records; no persistence yet):**
- `PieceRecord`, `NpcErrandRecord` and the `DeltaSnapshot` properties;
- the `WorldDelta` stores, readers and mutators, `TakeSnapshot` (`:520`), `FromSnapshot` order, and the two `TryApply`s plus the chest clause;
- effective-cell v2;
- `PlayerRecord.Factions` with the seven `With*` carries and player v10;
- `CompanionRecord.Route` and the two companion copy sites;
- simulation v2 and `CaptureRecord`;
- the allow-list names of D38.

Gate: E4, E5, `WorldDelta_ExposesNoPublicMutation` (`tests/Architecture.Tests/ArchitectureTests.cs:61-79`), and the existing `StateDumpTests`.

**S4 (persistence, landed once):**
1. `Sections/SchemaV13.cs`; the four repoints; the `SchemaV12.cs`, `SchemaV11.cs` and `SchemaV8.cs` headers.
2. `SectionCodec.cs`: the new DTOs and members, encode and decode, `Prove`, and `DecodeEntitySection` returning a snapshot.
3. `Migrations.cs`: `SchemaV13ToV14`, `Production`, `using V13`. `SaveModel.cs:20` → 14.
4. `SaveLoader.cs`: the decode site, the pass (§7 including the spill), the `with` rebuild, `ProveBaselines`.
5. `BaselineTransitions.cs`: pieces, errands, `with`.
6. `StateDump.cs`: the chest-key masking.
7. `tests/M2.Probe/M2Fixtures.cs`: the §9.1 world and player, the writer and current identities, the mirror.
8. Packs: `content-0.1.7/`, `content/` 0.2.9.
9. The fixture `v14/quick`, the `expected.json` files, `CanonicalState`, `HistoricalFixtureTests`, the `MigrationTests` edits, the new Persistence tests (E1-E5, §11, §12.1).
10. The documents of §13.

Gate:
- `dotnet test src/UNNAMED.sln` green;
- the 14 fixtures load complete and migrate through the commit path;
- each `expected.json` diff reviewed and recorded as "gained only the empty M7 fields".

**S2 (content), before S4:** the deadfall, `M6LayoutFingerprint`, the M7 transition and its two tests (§8.3). **S8/S10:** E6-E10 and §12.2.

---

## 15. Issues for other parts, and owner questions

**Owner questions:** none. Persistence raises nothing that changes a ruling or scope; D §6.1's five stand.

**Issues for other parts** (each is also in the structured result):
- **D:**
  - D14 omits the `SchemaV11ToV12` repoint (`Migrations.cs:701`), which becomes a compile error once `V12.Player.Companions` is typed `V13.Companion[]`.
  - D15's list should add the companion copy sites (`Companions.cs:135-141`, `:149-155`) and the seven `PlayerRecord.With*` initialisers (`PlayerState.cs:227-253`) as the same class of trap, with guard E4.
  - `DecodeEntitySection` should return a `DeltaSnapshot` (§6.1).
- **C:**
  - The v14 fixture must name the warden by the writer's ID, `npc.fixture.warden`, not `warden_sera` (C §14.6). The pack that wrote v11-v13 calls him that (`M2Fixtures.cs:132-134`), and the rename is what proves the pass reaches `via`. Use `npc.fixture.smith` as the Delvers' witness so no NPC serves two factions.
  - A discarded `via` must be a `Warning`, not `Loss`: the row survives, and `Resolve`'s Discarded branch always writes "dropped".
  - The merge clamp needs Domain constants (`FactionLedger.OrdinaryFloor`, `MaxPoints`) that FAC001 pins.
  - `FactionLedger`, `ActRecord`, `FactionKnowledge` and `FactionStanding` must expose no computed public instance properties.
- **B:**
  - Move the range checks of §9.2 from decode to `TryApplyPiece` / `TryApplyNpcErrand`, so one bad row is rejected rather than the entities section quarantined.
  - Add the definition-pass spill for a dropped storage piece.
  - `NpcSystem.Populate` must re-host an errand whose NPC's site moved to another cell (reported in `StructureAudit`), and drop, with a `StructureAudit` line, an errand for an NPC no longer placed.
  - `BuildingSystem.Populate` treats `door_open` on a non-door as false and reports it.
  - `TryApplyPiece` must not verify `Derived(…, Owner)` (the ownership-transfer seam).
  - `EncodeEntities`' `Prove` label becomes a string.
- **A:**
  - `NavRoute` exposes no computed public instance properties.
  - A companion route is validated fully at decode; an errand route's `Problem()` at apply.
  - State whether `config.navigation` is required whenever a region exists. The current fixture pack (region `fixture_vale`) must validate either way.
  - NAV004/NAV005 must compare against the built `MovementRules`, not raw `base_speeds` keys: the fixture pack omits the optional `stand_height_m`.
- **Content (B/C):**
  - The fixture pieces are named `piece.fixture.*`. BLD lints must not require the ID's second segment to equal `family`.
  - The fixture region gains a smith site at (32, 70), inside `location.den_mouth`'s radius, because FAC-M1 refuses an unplaced member (§8.2).
