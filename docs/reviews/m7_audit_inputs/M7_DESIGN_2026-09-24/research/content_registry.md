# M7 research notes - content registry, definition IDs, instance identity (key: `content_registry`)

Scope: how content definitions are declared, loaded, linted, built into domain catalogues and hashed; how runtime instance identity works; what M7 must touch to add **faction** definitions, **structure-piece** definitions and **structure instances**.

Evidence base: read-only snapshot of `origin/main` at `e10d2c4` (all `path:line` citations are repo-relative and valid at that commit). Read-only `git log/show` against `G:/UNNAMED` was used only to identify the commits that added earlier kinds. Untracked docs are labelled "untracked, authority unconfirmed". A scratch copy of `src/{Content,Domain,World,EntityRegistry}` and `content/` was built and linted in the session scratchpad (never in any `G:/UNNAMED*` tree) to confirm several "not enforced" findings empirically; those are marked **PROBE**.

Legend: **FACT** = read in code/docs, cited. **INFERENCE** = my reading/recommendation. **PROBE** = observed by running the linter on a scratch copy.

---

## 0. Top conclusions for the M7 designer

1. **`faction` is already a registered content kind** (`kind: faction`, directory `factions/`, ID prefix `faction`) and `faction_ref` is already a resolvable reference field. Adding faction content needs **no registry change** - only a builder/lint class, domain types, wiring and tests. FACT: `src/Content/SchemaResolution.cs:158-164`, `src/Content/ContentChecks.cs:34`. PROBE: a `content/factions/hollow/wardens.yaml` with a bare envelope lints clean today (103 definitions, 0 errors).
2. **There is no structure/building-piece content kind.** A new kind (e.g. `structure`, or a sub-kind) must be registered in `ContentKindRegistry`; an unregistered `content/structures/` directory raises `DIR003` and its files are not loaded at all. FACT `src/Content/ContentLoader.cs:501-536`; PROBE (see §3.6 - note the CLI still prints "Validation passed" and exits 0 in that case).
3. **`EntityKind.Building` (`bld`) already exists** for runtime building instances; there is **no prefix for a piece** although `PERSISTENCE.md` §5.4 says pieces are "ULID-keyed rows". FACT `src/Domain/EntityKind.cs:17,40`; `docs/PERSISTENCE.md:250`. Adding a prefix is cheap and does not touch any save fixture (IDs persist as strings) - see §4.
4. **Registry kind inference covers only `item`/`creature`/`npc`/`quest`.** `WorldDelta.PlaceCreated` (the only generic "player-created instance" path) calls the inferring overload, so a `structure.*` definition placed through it would throw `ArgumentException` unless M7 passes an explicit `EntityKind` or extends `TryInferFromDefinition`. FACT `src/Domain/EntityKind.cs:71-84`, `src/World/WorldDelta.cs:347-354`, `src/EntityRegistry/EntityRegistry.cs:78-85`.
5. **The loader enforces much less than `DATA_MODEL.md` claims.** Not enforced today: ID-prefix = kind, file-stem = ID, kind's directory = file's directory, `world.` prefix for world flags, envelope completeness, closed tag vocabulary (`_tags.yaml` does not exist), "no ULID-shaped values in YAML", definition `schema:` revision. PROBE-confirmed for all except multi-doc/anchors. An M7 kind that wants these guarantees must add them (or M7 should add them generically). See §9.
6. **A load-phase error hides all semantic lint.** The whole validation block (xref, aliases, every builder lint) runs only if every kind-directory file loaded. FACT `src/Content/ContentLoader.cs:137-140`. PROBE: a duplicate ID suppressed the SEM001 errors of a malformed item in the same pack.
7. **Builder-style lints are fail-fast and file-less.** `ITM001/CMB001/MAG001/CRF001/SOC001/QST001` wrap a whole builder in one `try` and report the first exception with no `FilePath`/line - contrary to DATA_MODEL M-5 "hard error with file and line". FACT e.g. `src/Content/SocialContent.cs:311-321`. Per-field line-accurate checks exist only in `ContentChecks` (`XREF*`, `MOD001`, `SEM*`).
8. **Every added definition breaks one test by design**: `ContentLoaderTests.LoadAll_Loads_Yaml_Files` asserts the exact sorted list of all 102 game definition IDs. FACT `tests/Content.Tests/ValidationTests.cs:508-547`.
9. **`content_hash` is the SHA-256 over every definition's ID + raw YAML text (comments included) + `_aliases.yaml`**; it is *not* a world-generation input, so adding faction/structure definitions never moves a cell baseline. Saves whose `content_hash` differs run the definition-ID pass, which blocks the load on any unresolved persisted ID. FACT `src/Content/ContentLoader.cs:762-775`, `src/Persistence/SaveLoader.cs:158`, `docs/PERSISTENCE.md:316`.
10. **World flags are cell-scoped**, not global: `GetFlag(CellKey cell, string flagId)`. Anything "global" (faction war state, a faction-wide flag) cannot be a plain `world.*` flag without choosing a host cell. FACT `src/World/WorldDelta.cs:250,261`. INFERENCE: faction state needs its own owned slice (S-27), not world flags.
11. **Today's "structures" are region-inline blocker rows with local non-definition IDs** (`rock_waystone`, `longhouse_north`, `fence_smithy`...), axis-aligned boxes or circles only; doors/switches/barriers/containers/stations are region-inline *site keys* (`door.*`, `switch.*`, `barrier.*`, `container.*`, `station.*`), validated by prefix, not content definitions. FACT `content/regions/ashen_hollow.yaml:63-199`, `src/Content/WorldContent.cs:146-285`, `src/Domain/Spatial/Blockers.cs:40-41`.

---

## 1. The content pipeline, end to end

### 1.1 Projects and direction of dependency (FACT)

- `src/Content/Content.csproj`: `OutputType Exe` (it is also the lint CLI), references **Domain** and **World**, package `YamlDotNet 15.1.0`. (`src/Content/Content.csproj`)
- `src/Domain/Domain.csproj`: **no project references at all** - Domain never sees Content, World, Application.
- `src/World/World.csproj` references Domain + EntityRegistry. `src/Application/Application.csproj` references Domain, EntityRegistry, World, Persistence, Content. `src/Presentation` references only Application + Domain (never Content).
- Architecture test: the registry assembly may reference only `UNNAMED.Domain` (`tests/Architecture.Tests/ArchitectureTests.cs:196-202`).
- Architecture test "no static mutable state" scans **Domain, Registry, World, Persistence** only - **not Content** (`tests/Architecture.Tests/ArchitectureTests.cs:86-104`, list at :89). INFERENCE: this is why `ContentKindRegistry`'s static `Dictionary` fields (`src/Content/SchemaResolution.cs:17-18`) do not fail it; do not move kind tables into World/Domain as static mutable collections.

### 1.2 Boot path (FACT, `src/Application/GameSession.cs:79-119`)

1. `new ContentLoader().LoadAll(options.ContentRoot)`; if `loader.HasErrors` -> `ContentBootException` listing every error (:81-84). Note: `HasErrors` (not the `LoadAll` return value) is what stops the game.
2. Builders produce domain/world records: `WorldContent.BuildLayout(loader, regionId)`, `BuildMovement`, `ProgressionContent.BuildRules`, `WorldContent.BuildTiers`, `TickMilliseconds`, then `ItemSetup(...)`, `CombatContent.Build(loader, regionId)`, `MagicContent.Build`, `CraftingContent.Build`, `SocialSetup(SocialContent.BuildNpcs, BuildDialogues){Companions = BuildCompanionTuning}`, `QuestSetup(QuestContent.BuildQuests)` (:86-99).
3. `ContentIdentity(options.ContentVersion, loader.ComputeContentHash(), loader.Definitions.Keys, loader.Aliases, loader.Removed, loader.Discarded)` (:100-101). `ContentVersion` default `"0.3.0"`, diagnostic only (:21).
4. The generator (`CellBaselineGenerator(GenerationProfile(...))`) is built from the layout's terrain rule and **fixed nodes only** (:102-107); baseline transitions registered (:108-117).
5. Display names: `WorldContent.DisplayNames(loader)` = every definition's `name` field (`src/Content/WorldContent.cs:96-100`), exposed as `GameSession.DisplayName(definitionId)` (:162).

### 1.3 `ContentLoader.LoadAll` order (FACT, `src/Content/ContentLoader.cs:73-201`)

1. Root must exist else `LOAD001` (:78-88).
2. Top-level `*.yaml` in the content root (non-`_`) are loaded with no directory constraint (test-fixture convenience) (:95-108).
3. For each registered kind (dictionary insertion order), scan its `Directory` recursively for `*.yaml`, skipping `_`-prefixed files; a directory shared by several kinds (`items`) is scanned once with the **first** kind registered for it (:113-134, :206-238). A missing directory is silently skipped (:119-123).
4. Per file (`LoadFile`, :319-392): `ContentYamlDeserializer.Deserialize` -> `ID001` grammar (:397-413) -> `KIND001` kind in registry (:418-434) -> `ValidateKindDirectoryMatch` (:439-495) -> `DUP001` duplicate (:347-359) -> register in `_definitions` (keyed by ID only).
5. **Only if every kind-directory file loaded** (`directoryLoadSuccess`, :137-140):
   - `ValidateUnknownDirectories` -> `DIR003` for any non-`_` top-level directory not in `KnownDirectories` (:143, :501-536). **Its errors do not clear `success`** (see §3.6).
   - `LoadAliasMap(_aliases.yaml)` -> `ALIAS001/003/004/005` (:146, :541-626).
   - `ValidateCrossReferences` - legacy: only checks *tags* that look like IDs (`XREF001`) (:149, :632-689); tag vocabulary is a TODO (:651).
   - `ValidateDuplicateIds` -> `DUP002` (:152, :694-721).
   - Then the domain validators, in this order: `ProgressionContent` (PRG) :155-157, `ContentChecks` (XREF002-005, MOD001, SEM) :160-162, `ItemContent` (ITM) :165-167, `WorldContent` (WLD) :170-172, `CombatContent` (CMB) :175-177, `MagicContent` (MAG) :180-182, `CraftingContent` (CRF) :185-187, `SocialContent` (SOC) :190-192, `QuestContent` (QST) :195-197.

### 1.4 The envelope and the deserializer (FACT)

- `ContentEnvelope` (`src/Content/ContentDefinition.cs:55-128`): `schema` (int, default `SchemaVersion.Current = 1`, :12-15), `id`, `kind`, `display_key`, `tags` (string[]), optional `notes`, `defines`, `deprecated`, `alias_of`; internal `YamlSource`, `SourceFile`, `LineOffset`.
- `ContentYamlDeserializer.Deserialize` (`src/Content/ContentYamlDeserializer.cs:109-189`) deserializes the file into `Dictionary<string, object>` and copies only the envelope fields; **all kind-specific fields stay in `YamlSource`** and are re-parsed by each builder (`Read(definition.YamlSource)` -> `Dictionary<object, object>`, e.g. `src/Content/CombatContent.cs:451-452`).
- INFERENCE (strong): YamlDotNet returns untyped scalars as `string`, which is why every builder parses numbers with `as string` + `TryParse` (e.g. `src/Content/ItemContent.cs:252-255`). Therefore `schemaObj is int` (`ContentYamlDeserializer.cs:154`) and `definesObj is object[]` (:164) never match: **`schema:` and `defines:` are never read**; every envelope reports `Schema = 1`. PROBE: a faction with `schema: 99` lints clean.
- `SchemaTypeMapper` (`src/Content/SchemaTypeMapper.cs:19-137`) and its empty `*Schema` records (:143-397) are **not in the load path** (only tests call them, `tests/Content.Tests/ValidationTests.cs:353-375`); `GetKind(typeof(ContentEnvelope))` returns the placeholder `"zone"` (:107). `skill` was never added to it (M2c diff), confirming it is vestigial. INFERENCE: M7 need not touch it.

### 1.5 Error-code families (FACT, grep of `src/Content`)

`LOAD001-003`, `ID001`, `KIND001`, `DIR001-004`, `DUP001-002`, `ALIAS001-005`, `XREF001-005`, `MOD001`, `SEM001-003`, `PRG001-005`, `ITM001`, `WLD001-014`, `CMB001`, `MAG001`, `CRF001`, `SOC001`, `QST001`. M7 would add e.g. `FAC001` / `STR001` (INFERENCE; names free).

`ValidationError` = `{SeverityLevel, Code, Message, FilePath, LineNumber?, DefinitionId?}`, `ToString` -> `"ERROR CODE: msg [file:line]"` (`src/Content/IDValidation.cs:79-129`). Only Error severity is used in practice.

### 1.6 CLI (FACT)

`src/Content/Program.cs:4-78`: commands `lint`, `schema-dump`, `xref`, `help`; options `-c/--content-root <path>` (space-separated only; `--content-root=...` is ignored, AGENTS.md), `-f/--format text|json`. `schema-dump` prints the kind table (kind, full kind, directory, ID prefix), **not** JSON Schema per kind (`src/Content/Cli/ContentCliApp.cs:66-104`). `xref` lists definitions grouped by kind, not a reference graph (:109-152). `lint` exit code follows `LoadAll`'s return value, not `HasErrors` (:19-61). CI (`.github/workflows/dotnet.yml:24-34`) runs only restore/build/test; content lint in CI happens through `LoadAll_Loads_Yaml_Files`, which asserts `Assert.Empty(loader.Errors)` (`tests/Content.Tests/ValidationTests.cs:515`).

---

## 2. Content kinds today and their ID shapes

### 2.1 Definition-ID grammar (FACT)

- Single source of truth: `UNNAMED.Domain.DefinitionId` regex `^[a-z0-9]+(\.[a-z0-9_]*[a-z_][a-z0-9_]*)+(\.[0-9]{2})?$` (`src/Domain/DefinitionId.cs:19-20`); the Content linter delegates to it (`src/Content/IDValidation.cs:21`: "content and runtime share one contract").
- Meaning: >= 2 segments; first segment `[a-z0-9]+`; each later segment `[a-z0-9_]` containing at least one letter or underscore (so `item.123` is invalid, `r_0_0`-style digits-plus-underscore segments are fine); optional trailing two-digit ordinal `.NN`. Tests: `tests/EntityRegistry.Tests/EntityRegistryTests.cs:9-24`.
- Doc grammar: `<kind>[.<subkind>]*.<snake_case_name>[.<ordinal>]`, "IDs are a public API", renames only via append-only `content/_aliases.yaml` (`docs/DATA_MODEL.md:96-119`; D-04 `docs/DECISIONS.md:96-115`).
- `DefinitionId` is a `readonly record struct` with `IsValid`, `Parse` (throws `ArgumentException`), `TryParse`, `FromString` (no validation) (`src/Domain/DefinitionId.cs:17-71`).
- Reserved mod namespace: `<kindIdPrefix>.mod.*` for **every** registered kind, enforced by `MOD001` (`src/Content/ContentChecks.cs:89-102`). A new kind is covered automatically once registered.

### 2.2 The registered kind table (FACT, `src/Content/SchemaResolution.cs:57-297`)

28 kinds, 26 directories (`items` shared by three kinds). "In use" = has files in `content/` at e10d2c4 (102 definitions total, `tests/Content.Tests/ValidationTests.cs:516-546`).

| kind | directory | IdPrefix | builder / lint | files | ID shape in use (examples) |
|---|---|---|---|---|---|
| `item` | `items` | `item` | `ItemContent` (ITM), `ContentChecks.PhaseOneSchemas` (SEM) | 12 | `item.<category-ish>.<name>`: `item.ammo.arrow_rough`, `item.material.iron_ingot`, `item.consumable.salve_minor`, `item.quest.halda_token`, `item.tome.resonance_primer`, `item.tool.water_flask`, `item.trinket.wolf_fang` |
| `item.weapon` | `items` | `item.weapon` | same | 3 | `item.weapon.rusted_sword`, `item.weapon.hunting_bow`, `item.weapon.march_spear` |
| `item.armor` | `items` | `item.armor` | same | 2 | `item.armor.hide_cap`, `item.armor.hide_vest` |
| `creature` | `creatures` | `creature` | `CombatContent` (CMB), SEM | 6 | `creature.<family>.<name>`: `creature.beast.wolf_grey`, `creature.undead.bone_walker_husk`, `creature.construct.animated_armour` |
| `npc` | `npcs` | `npc` | `SocialContent` (SOC) | 4 | `npc.<place>.<person>`: `npc.ashen_hollow.kera_voss`, `.renn_vale`, `.sel_arien`, `.tavar_orr` |
| `spell` | `spells` | `spell` | `MagicContent` (MAG) | 3 | `spell.<domain>.<name>`: `spell.force.impulse_bolt`, `spell.warding.brace_ward`, `spell.vital.mending_thread` |
| `ability` | `abilities` | `ability` | `CombatContent` | 7 | `ability.creature.<name>`: `ability.creature.wolf_bite` |
| `effect` | `effects` | `effect` | `CombatContent` | 5 | flat `effect.<name>`: `effect.bleeding`, `effect.weakened` |
| `recipe` | `recipes` | `recipe` | `CraftingContent` (CRF) | 2 | `recipe.<discipline>.<name>`: `recipe.smithing.march_spear` |
| `resource` | `resources` | `resource` | `CraftingContent` | 2 | `resource.<class>.<name>`: `resource.ore.iron`, `resource.wood.ash` |
| `quest` | `quests` | `quest` | `QuestContent` (QST) | 2 | `quest.<place>.<name>`: `quest.ashen_hollow.iron_under_ash`, `.three_quiet_stones` |
| `dialogue` | `dialogue` | `dialogue` | `SocialContent` | 4 | `dialogue.<place>.<person>` (mirrors the NPC ID) |
| **`faction`** | **`factions`** | **`faction`** | **none** | **0** | doc example `faction.settlement.stoneford_covenant` (`docs/DATA_MODEL.md:102,564`) |
| `loot` | `loot` | `loot` | `ItemContent` | 6 | flat `loot.<name>`: `loot.wolf_grey`, `loot.den_cache` |
| `merchant` | `merchants` | `merchant` | `ItemContent` | 1 | `merchant.ashen_hollow.kera_voss` (mirrors NPC ID) |
| `affix` | `affixes` | `affix` | none | 0 | doc: `affix.weapon.keen` |
| `node` | `nodes` | `node` | `CraftingContent` | 2 | `node.<class>.<name>`: `node.ore.iron_seam`, `node.wood.ash_stand` |
| `spawn` | `spawns` | `spawn` | `CombatContent` | 8 | `spawn.hollow.<name>`: `spawn.hollow.den_pack` |
| `location` | `locations` | `location` | `WorldContent` (WLD) | 6 | flat `location.<name>`: `location.outpost`, `location.blackvein_cut` |
| `config` | `config` | `config` | per-group builders | 13 | flat `config.<group>` (see §7) |
| `skill` | `skills` | `skill` | `ProgressionContent` (PRG) | 7 | flat `skill.<name>` by rule (`docs/DATA_MODEL.md:773`): `skill.one_hand_blade` |
| `species` | `species` | `species` | none | 0 | - |
| `schedule` | `schedules` | `schedule` | none | 0 | - |
| `set` | `sets` | `set` | none | 0 | - |
| `region` | `regions` | `region` | `WorldContent` | 1 | `region.ashen_hollow` |
| `anchor` | `anchors` | `anchor` | none | 0 | - |
| `fact` | `facts` | `fact` | none | 0 | - |
| `world_flag` | `world_flags` | **`world`** | `WorldContent.CheckFlag` (WLD006) | 6 | `world.<area>.<flag>`: `world.hollow.longhouse_door_open`, `world.foldscar.steadied` |

Notes (FACT unless marked):
- The kind table is also documented as "closed and normative" (`docs/DATA_MODEL.md:36`) with the bijection claim "every directory ... maps to exactly one kind ... and every kind ... to exactly one directory". Code: `items` maps to three kinds; `KindByDirectory` keeps the first (`src/Content/SchemaResolution.cs:290-296`).
- `display_key` convention in content: always `<id>.name` (every file read; e.g. `content/npcs/ashen_hollow/kera_voss.yaml:4`). Localization is not built; player-visible text is inline (`name:`, dialogue `text:`).
- `tags` in content are mostly just the kind word (`[item]`, `[config]`, `[npc]`); creatures carry gameplay tags that effects test (`tags: [creature, undead]`, `content/creatures/undead/bone_walker_husk.yaml:5`; consumed at `src/Content/CombatContent.cs:284`).
- Second ID segment of items/creatures is a free namespace, not a sub-kind: `item.ammo.*` is `kind: item` (`content/items/ammo/arrow_rough.yaml`). Only `item.weapon`/`item.armor` are true sub-kinds with schemas.
- Content subdirectories under a kind directory are free-form (`items/weapon/`, `npcs/ashen_hollow/`, `world_flags/foldscar/`), since the loader recurses (`src/Content/ContentLoader.cs:213`).

### 2.3 Non-definition, definition-shaped keys already in use (FACT)

These are strings that look like definition IDs (and some are registered as the "definition" of a runtime instance) but are **not content definitions**, are not in `ContentIdentity.DefinitionIds`, and are validated only by prefix or uniqueness:

| key family | where authored/derived | check | notes |
|---|---|---|---|
| `door.<name>` | region `doors[].key` | WLD004 prefix + unique (`src/Content/WorldContent.cs:159,163`) | open state = a `world.*` flag in the door's cell |
| `container.<name>` | region `containers[].key` | WLD009 (`:194-200`) | registered in the Entity Registry as the *definition* of a `cnt_` instance: `registry.CreateEntity(DefinitionId.Parse(site.Key), EntityKind.Container)` (`src/World/Runtime/Items.cs:518`) |
| `station.<name>` | region `stations[].key` + free-text `kind` (`forge`, `anvil`) | WLD011 (`:222-226`) | recipes name a station *kind* string, `station: anvil` (`content/recipes/smithing/march_spear.yaml:8`; `src/Content/CraftingContent.cs:118`) - no station definitions exist |
| `switch.<name>` | region `switches[].key` | WLD013 (`:256-262`) | stands on a region structure by local id |
| `barrier.<name>` | region `barriers[].key` | WLD014 (`:278-285`) | lifted only by a switch in its cell |
| structure local ids (`rock_waystone`, `longhouse_north`, `fence_smithy`, `tree_17`) | region `structures[].id` | unique within region (WLD002, `:146-148`) | not dotted; presentation keys art on them (`src/Presentation/Art/art_bindings.json` sections `structures`, `structure_prefixes: tree_`, `building_walls: longhouse_, forge_`) |
| node names (`iron_seam`) | region `nodes[].name` | snake_case, unique (WLD010, `:209-215`) | generator semantic key |
| `corpse.<spawner>.m<i>_g<gen>` | derived (`src/World/Runtime/Creatures.cs:155-160`) | - | "a valid definition-ID-shaped name" used as a container key |
| `node.<cellkey>.<rule>.<ordinal>` | derived node keys (`docs/PERSISTENCE.md:256`) | - | not stored ULIDs |

INFERENCE for M7: player-built structures and pieces should follow the **definition-ID + ULID instance** route (content piece definitions, `bld_`/new-prefix instances), not the region-local-key route; but authored settlement structures (walls of the longhouse) remain region-inline blockers unless M7 migrates them. Presentation already depends on the local ids via `art_bindings.json`, so renaming authored structure ids is a presentation-data change (not a content-hash or save change: `src/Presentation/Art/ArtBindings.cs:28-31`).

---

## 3. Recipe: how to add a content kind (and a builder for an existing kind)

### 3.1 Worked examples in history (FACT, read-only `git show --stat` on e10d2c4 ancestry)

- **New kind `skill` - M2c, commit `74c2bc9`.** Touched: `src/Content/SchemaResolution.cs` (+8: the `kinds["skill"]` entry, now `:223-229`), `src/Content/ContentLoader.cs` (+5: the `ProgressionContent.Validate` call, now `:155-157`), new `src/Content/ProgressionContent.cs`, `content/skills/*.yaml` and `content/config/*.yaml`, `tests/Content.Tests/ValidationTests.cs` (the exact-ID list), new `tests/Content.Tests/ProgressionContentTests.cs` and `ProgressionBalanceTests.cs`, domain `src/Domain/Progression/*`, persistence schema 4 (because the player record gained progression), `tests/Persistence.Tests/Fixtures/content-0.1.1/skills/athletics.yaml` (fixture pack gained what the v4 fixture references), docs (`DATA_MODEL.md` kind table row + §4.21, `M2C_STATUS.md` :35 "A `skill` kind (`content/skills/`)"). `SchemaTypeMapper` was **not** touched.
- **`skill_ref` reference field - pre-M3b hardening, commit `3317e3d`** (`ContentChecks.cs` created; `src/Content/ContentLoader.cs` +5 for its call; `tests/Content.Tests/ContentChecksTests.cs`; and fixture content packs were repaired because the stricter lint rejected their dangling refs: `tests/Persistence.Tests/Fixtures/content/...`). Lesson: **tightening lint can break the persistence fixture content packs**, adding a kind cannot.
- **Builder for already-registered kinds `npc`/`dialogue` - M4, commit `d8fe385`**: `src/Content/SocialContent.cs` (new, 240 lines), `ContentLoader.cs` (+5), `WorldContent.cs` (+18: region `npcs` sites, WLD012), `src/Domain/Social/Social.cs` (new), `src/World/Runtime/Social.cs` (new: `SocialSetup` + `NpcSystem`/`DialogueSystem`), `src/Application/GameSession.cs` (+1 wiring), content files, `tests/Content.Tests/SocialContentTests.cs`, `ValidationTests.cs` list, `DATA_MODEL.md` "As implemented (M4)", fixture content pack additions for the schema-10 fixture (`.../Fixtures/content/npcs/fixture/warden_sera.yaml`, `.../dialogue/fixture/warden_sera.yaml`) and fixture `_aliases.yaml`. **This is the closest template for factions.**
- **`node_ref` + node/recipe builders for registered kinds - M3f, commit `5a9d45f`**: `ContentChecks.cs` (+1 line: `["node_ref"] = new[] { "node" }`, now `:43`), `CraftingContent.cs` (new), `WorldContent.cs` (+28: region `nodes`/`stations` lists), `RegionLayout.cs` (+15), `World/Generation.cs` (fixed nodes enter the generator), `GameSession.cs`, persistence schema 9. **Closest template for structure pieces that enter the world/region.**

### 3.2 Step list - registering a brand-new kind (e.g. structure pieces)

1. **Register the kind** in `ContentKindRegistry`'s static constructor: `kinds["<kind>"] = new ContentKindDefinition(Kind: "<kind>", FullKind: "<kind>", Directory: "<dir>", IdPrefix: "<prefix>")` (`src/Content/SchemaResolution.cs:57-297`, record at :352). Effects: `KIND001` accepts it; `<dir>` becomes a known directory (no `DIR003`); the loader scans `<dir>` (:116-134 of ContentLoader); `MOD001` reserves `<prefix>.mod.*`. For a sub-kind sharing a directory (like `item.weapon`), register each full kind with the same `Directory`.
2. **Reference fields**: add `["<name>_ref"] = new[] { "<kind>"[, "<sub-kind>"...] }` to `ContentChecks.ReferenceSuffixes` (`src/Content/ContentChecks.cs:18-44`). Matching is by suffix with role prefixes (`hall_structure_ref` would match `structure_ref`; `_refs` = list) (:154-163). Target-kind match is **exact** (`kinds.Contains(target.Kind)`, :178), so list sub-kinds explicitly as `item_ref` does (:20). Without this, any `*_ref` naming the kind is `XREF004` (PROBE: `hall_structure_ref` -> `XREF004 ... target kind is unknown`).
   - Gotcha: `tests/Content.Tests/ContentChecksTests.cs:119-121` uses **`station_ref`** as the canonical "unknown reference field" - if M7 introduces a `station` kind/`station_ref`, that test must change.
   - Gotcha: any YAML map containing both scalar `kind` and `ref` keys is treated as a reward/grant entry and `kind` must be a reward kind (`XREF005`) (:111-117). Avoid `{kind: ..., ref: ...}` shapes in piece/faction schemas unless they are rewards.
   - If the kind can be a quest reward `{kind, ref}`, add it to `ContentChecks.RewardKinds` (:58-69) **and** to Domain `RewardKinds.Built` (`src/Domain/Quests/Quests.cs:203-207`) plus a `QuestContent.Reward` case (`src/Content/QuestContent.cs:187-206`).
3. **Builder + lint class** `src/Content/<Area>Content.cs` (pattern of every builder):
   - `public static IReadOnlyList<ValidationError> Validate(ContentLoader loader)` - calls the builders inside `Try(...)`.
   - `public static <DomainType> Build...(ContentLoader loader)` - `loader.GetByKind("<kind>")` (`src/Content/ContentLoader.cs:726-730`, exact-kind filter), `Read(definition.YamlSource)`, typed readers from `using static UNNAMED.Content.CombatContent;` (`Text`, `Int`, `Number`, `Mm`, `Rows`, `Map`, `List`, `Config`, `ToTicks`, `IntOf`: `src/Content/CombatContent.cs:423-489`), throwing `FormatException`/`KeyNotFoundException` with an ID-prefixed message; return `ImmutableSortedDictionary<string, T>` keyed by ID with `StringComparer.Ordinal` (e.g. `src/Content/SocialContent.cs:69-94`).
   - Cross-kind existence checks via a local `Defined(loader, id, kind, at)` that accepts sub-kinds (`src/Content/SocialContent.cs:293-296`).
   - Refuse fields that are named by DATA_MODEL but not built ("X is not built in Phase N") - pattern: `SocialContent.cs:85-89`, `CraftingContent.cs:122-126`, `QuestContent.cs:28-29,58-64` (Quest also rejects *any* unknown key: `'{key}' is not a quest field`). INFERENCE: NPC/creature/item builders silently ignore unknown keys; a closed field set (Quest style) is the stricter, better pattern for new M7 kinds.
   - `Try` wrapper -> one code, no file/line, fail-fast (see §0.7). If line-accurate errors are wanted, add a `PhaseOneSchemas`-style checker in `ContentChecks` (`src/Content/ContentChecks.cs:223-430`, `Fields` helper with `OneOf/Integer/Number/Range/KeysOf/Require/Present`), which DATA_MODEL anticipates: "Each later schema gains its checks in the milestone that first authors it" (`docs/DATA_MODEL.md:825`).
4. **Hook the validator** into `ContentLoader.LoadAll`'s validation block with the 3-line pattern (`src/Content/ContentLoader.cs:155-197`): `var e = XContent.Validate(this); _errors.AddRange(e); success &= e.Count == 0;`.
5. **Domain types** in `src/Domain/<Area>/` - immutable records and pure rule functions, no I/O, no references (Domain has none). Existing analogues: `NpcDefinition` (`src/Domain/Social/Social.cs:10-16`), `QuestDefinition`, `NodeDefinition`/`RecipeDefinition` (`src/Domain/Crafting/Crafting.cs`), `RegionLayout`/site records (`src/Domain/Spatial/RegionLayout.cs:12-98`).
6. **World setup**: a `<Area>Setup` record with a static `Empty` in `src/World/Runtime/<Area>.cs` (pattern `src/World/Runtime/Social.cs:17-25`, `Items.cs:13-25`) and an init property on `SimulationSetup` defaulting to `Empty` (`src/World/Runtime/Simulation.cs:14-33`). Systems read it through `SystemContext.Setup` (`src/World/Runtime/Systems.cs:24-36`).
7. **Application wiring** in `GameSession.Boot` (`src/Application/GameSession.cs:88-99`).
8. **Content files**: `content/<dir>/[<subdir>/]<stem>.yaml`, one definition per file; envelope `id`, `kind`, `schema: 1`, `display_key: <id>.name`, `tags`, `notes`; `name:` for display.
9. **Tests**:
   - Update the exact ID list in `tests/Content.Tests/ValidationTests.cs:516-546` (mandatory; breaks on any added/renamed definition).
   - Optionally extend `KnownDirectories_Is_Closed_Set` (`tests/Content.Tests/ValidationTests.cs:332-350`; it already asserts `factions`).
   - New `tests/Content.Tests/<Area>ContentTests.cs` using either the "copy game content and edit one file" harness (`EditedContent`/`ErrorsWith`, `tests/Content.Tests/QuestContentTests.cs:21-50`) or the temp-pack harness (`tests/Content.Tests/ContentChecksTests.cs:36-50`); `RepoPaths.Root()` locates the repo.
   - Application/World tests that boot game content (`tests/Application.Tests/*`, `tests/World.Tests/TestWorlds.cs:51-57`) keep working if the new builder tolerates packs without the kind.
   - Persistence fixture content packs (`tests/Persistence.Tests/Fixtures/content`, `content-0.1.0`..`content-0.1.6`) need edits **only** if (a) a persisted record in a new fixture references the new kind's IDs, or (b) a tightened generic lint rejects something in them. Adding a kind is invisible to them.
10. **Docs**: `docs/DATA_MODEL.md` §1 kind table (:45-57) and/or "referenced kinds" table (:59-69), a §4.x schema with an "As implemented (M7)" paragraph (the house style of §4.4-§4.21), and a §5 reference-field row (:806-819).

### 3.3 Step list - factions (kind already registered)

FACT inventory of what already exists:
- Kind/dir/prefix registered (`src/Content/SchemaResolution.cs:158-164`); `factions` is a known directory (asserted in `tests/Content.Tests/ValidationTests.cs:348`).
- `faction_ref` resolves to `faction` (`src/Content/ContentChecks.cs:34`) - works at any depth, with role prefixes (`enemy_faction_ref`, `member_faction_refs`). PROBE: `enemy_faction_ref: faction.test.nobody` -> `XREF003`.
- Legacy tag check treats a tag starting `faction.` as a reference (`src/Content/ContentLoader.cs:665`).
- Quests refuse `faction_ref` ("not built in Phase 1") (`src/Content/QuestContent.cs:28-29`; test `tests/Content.Tests/QuestContentTests.cs:118-125`).
- NPC builder does **not** refuse `faction_ref` - it is simply ignored (refused list is `schedule_ref, anchors, combat_profile, relationships_init, name_pool`, `src/Content/SocialContent.cs:85-89`). PROBE: adding `faction_ref: faction.hollow.wardens` to Kera Voss lints clean when the faction exists, `XREF003` when it does not. Creature builder also ignores unknown keys.
- Not-yet-built vocabulary reserved for M7: quest objective types `faction_reputation`, `faction_state` -> "factions (M7)", `construct_building` -> "building (M7)" (`src/Domain/Quests/Quests.cs:141-158`, lines :146, :154-155); reward kind `reputation` not built (:205-206) though `ContentChecks.RewardKinds` lists `reputation` with a null target (no ref check) (`src/Content/ContentChecks.cs:60`).
- Dialogue: Phase-1 closed lists are `Conditions = visited, world_state, has_item, relationship, skill, level, quest_state, companion_present` and `Consequences = transfer_item, give_recipe, set_world_flag, record_relationship_event, open_service, start_quest, recruit_companion, order_companion` (`src/Content/SocialContent.cs:26-30`); DATA_MODEL's full closed set additionally names `reputation`, `faction_state` conditions and `add_reputation` consequence (`docs/DATA_MODEL.md:525-529`).
- Personal relationships already exist as a separate model: dimensions `affection, fear, grudge, respect, trust`, each in `[-100, 100]`, "there is no single good-or-evil meter" (`src/Domain/Social/Social.cs:25-38`).

Steps (INFERENCE): steps 3-10 of §3.2 (no step 1; step 2 only for new ref names such as `territory_region_refs` - `region_ref` already exists (`ContentChecks.cs:30`) - or a new reward kind); decide whether NPC/creature `faction_ref` becomes built (then it must be *read*, and quests' refusal lifted); extend dialogue/quest closed vocabularies deliberately.

DATA_MODEL's faction schema to reconcile (FACT, `docs/DATA_MODEL.md:557-581`): fields `attitude_default: {faction_ref: value in [-1,1]}`, `members: [npc.*]`, `player_start_reputation`, `reputation_tiers: [{tier, min, services, dialogue_flags}]`, `territory: [region|cell]`, `laws: [{offense, response, bounty_base}]`, `services_gated`, `joinable`, `join_requirements?`, `enemy_of: [faction.*]`. Lint implications (INFERENCE): `attitude_default` keys and `members`/`enemy_of` values are **not** `*_ref` fields, so the generic walker will not check them - the faction builder must; the example's `territory: [..., cell.stoneford_town]` uses a `cell.` "ID" that is not a content kind; the example mixes a `[-1,1]` attitude scale with integer reputation mins (-100..400).

### 3.4 Step list - structure-piece definitions (new kind)

INFERENCE (options for the designer, grounded in the above):
- Kind naming: `structure` (dir `structures`, prefix `structure`) with IDs like `structure.timber.wall`, or sub-kinds sharing one directory (`structure.wall`, `structure.door`, `structure.foundation`) if per-sub-kind required fields are wanted (the item/item.weapon precedent). Sub-kinds must be listed individually in any `*_ref` target set.
- Stations: today stations are region-inline `{key, kind, position_m}` with a free-text station kind matched by recipes' `station:` string. ROADMAP M7 wants "one crafting station as a placeable" (`docs/ROADMAP.md:283`). A piece definition carrying a `station: forge` capability string would plug into `RecipeDefinition.Station` without a new kind; a `station_ref` would collide with the test in §3.2 step 2.
- Doors: today a door is a region-inline box + a cell-scoped `world.*` flag (WLD004). Player-built doors cannot reuse that (flags are declared content and cell-scoped); INFERENCE: door open/closed state for pieces belongs on the piece instance record.
- Geometry: domain blockers are **axis-aligned boxes or circles** with `HeightMm` and optional `ClearanceMm` (`src/Domain/Spatial/Blockers.cs:11-14,40-41,95`), parsed from `box_m: [min_x, min_z, max_x, max_z]` / `circle_m: [x, z, r]` + `height_m` (+ `clearance_m`) in metres (`src/Content/WorldContent.cs:347-372`). A piece footprint schema can reuse these shapes if rotations are restricted to 90-degree steps (INFERENCE; conflicts with "free rotation", §9).

### 3.5 What adding a kind does NOT require (FACT/INFERENCE)

- No change to `SchemaTypeMapper` (vestigial, §1.4).
- No change to persistence fixtures or `schema_version` merely for a kind; persistence changes come from new *persisted state* (faction standing, building records), which is a separate axis (`docs/DATA_MODEL.md:833-857`).
- No worldgen change: content is not a generation input unless placed into `GenerationProfile` (`src/World/Generation.cs:29-60`).

### 3.6 Gotchas observed (PROBE)

- Unregistered directory: `content/structures/timber.yaml` -> the file is **not loaded** (102 definitions), `DIR003` is reported, yet the CLI prints **"Validation passed" and exits 0** because `ValidateUnknownDirectories` never clears `success` (`src/Content/ContentLoader.cs:143`; CLI `src/Content/Cli/ContentCliApp.cs:48-57`). `GameSession.Boot` would still refuse (it checks `HasErrors`), and the ID-list test would fail. Do not rely on lint exit code alone.
- Duplicate ID (load error) + a malformed item in the same pack: only `DUP001` is reported; the item's `SEM001` errors never run (gate at `ContentLoader.cs:140`). The DUP001 message says "found in both <original> and <path>" - the first file's path is not named (`:350-356`), contrary to "hard error naming both files" (`docs/DATA_MODEL.md:827`).

---

## 4. Instance identity: `EntityKind`, `EntityId`, `EntityRegistry`

### 4.1 `EntityKind` and prefixes (FACT, `src/Domain/EntityKind.cs:12-50`)

| EntityKind | prefix | inferred from definition? | used in code today |
|---|---|---|---|
| `Item` | `itm` | yes (`item.*`) | inventory, containers, ground stacks |
| `Npc` | `npc` | yes (`npc.*`) | derived IDs (`src/World/Runtime/Social.cs:121-125`) |
| `Creature` | `crt` | yes (`creature.*`) | derived IDs (`src/World/Runtime/Creatures.cs:162-166`) |
| **`Building`** | **`bld`** | no | **no runtime use yet** |
| `Container` | `cnt` | no | world containers (`src/World/Runtime/Items.cs:518`) |
| `QuestInstance` | `qst` | yes (`quest.*`) | - |
| `Corpse` | `crp` | no | - (corpses are containers keyed `corpse.*`) |
| `TravelAnchor` | `anc` | no | - |
| `Summon` | `sum` | no | - |
| `FarmPlot` | `plt` | no | - |
| `WorldEvent` | `evt` | no | used only to mint random ULID tokens for save temp names (`src/Persistence/SaveStore.cs:141,391,544`) |
| `Character` | `chr` | no | player (`src/Application/GameSession.cs:139`); an "assumption ... pending the owner's decision" (`src/Domain/EntityKind.cs:26-30`), recorded as added in `docs/M2_STATUS.md:73` and `docs/DATA_MODEL.md:123` |

- The doc list matches exactly: `docs/DATA_MODEL.md:123` ("`itm` item, `npc` NPC, `crt` creature, `bld` building, `cnt` container, `qst` quest instance, `crp` corpse, `anc` travel anchor, `sum` summon, `plt` farm plot, `evt` world-event instance, `chr` player character (added by M2)").
- The enum doc comment: kind "is a property of the INSTANCE, not of its definition: a corpse (`crp`) is created from a creature definition" (`src/Domain/EntityKind.cs:7-11`).
- `TryInferFromDefinition` maps only the first ID segment `item|creature|npc|quest` (`src/Domain/EntityKind.cs:71-84`); tests pin that `node.*` is not inferable and that an ambiguous definition needs an explicit kind (`tests/EntityRegistry.Tests/EntityRegistryTests.cs:217-238`).

### 4.2 `EntityId` format (FACT, `src/Domain/EntityId.cs`)

- `"<prefix>_<ULID>"`, prefix exactly **3** chars (`PrefixLength = 3`, :26), ULID 26 Crockford-base32 chars (:25), canonical uppercase; parse accepts lowercase and normalizes; first ULID char must be <= `'7'` (:91). `Kind`, `Ulid`, `Value`, `Timestamp` (decoded ms).
- `NewId(kind)` = wall-clock ms (`DateTimeOffset.UtcNow`) + 80 bits from `RandomNumberGenerator` (:48-53) - **non-deterministic**.
- `Create(kind, timestampMs, random[10])` - explicit parts "for fixtures and tests; runtime code uses NewId" (:55-70) - but runtime uses it for **derived** identities: NPCs `EntityId.Create(EntityKind.Npc, 1, hash("unnamed.npc/v1", npcId)[0..10])` (`src/World/Runtime/Social.cs:121-125`), creatures `EntityId.Create(EntityKind.Creature, 1, hash("unnamed.creature/v2", key, generation)[0..10])` (`src/World/Runtime/Creatures.cs:162-166`). M4 documents "Identity is derived from the NPC's ID" (`docs/M4_STATUS.md:56`).
- JSON converter (:146-160); equality ordinal, null-safe operators (:122-143).
- `TryParse` accepts a prefix iff some `EntityKind` enum member's `Prefix()` equals it, by iterating `Enum.GetValues<EntityKind>()` (`src/Domain/EntityKind.cs:52-64`).

### 4.3 `EntityRegistry` (FACT, `src/EntityRegistry/EntityRegistry.cs`)

- D-10: "owns creation, ULID assignment, and lookup ... stores no gameplay state" (`docs/DECISIONS.md:218-230`); code header says the same (:1-4, :19-30).
- `EntityInstance(DefinitionId DefinitionId, EntityId InstanceId)` (:17).
- API: `CreateEntity(DefinitionId, EntityId)` (explicit ID; throws on duplicate ID, :44-66); `CreateEntity(DefinitionId, EntityKind)` (fresh `NewId`, :71-72); `CreateEntity(DefinitionId)` (infers kind, throws `ArgumentException` "pass an EntityKind explicitly", :78-85); `TryGetEntity`/`GetEntity`/`Exists` (dead excluded, :93-127); `DestroyEntity` = tombstone into `_deadEntities` (:133-142); `GetEntitiesByDefinition` (:149-159); `EntityCount`, `GetAllEntities`, `Clear` (:164-199); string extension overloads (:205-240). Internally locked (`_lock`).
- The registry does **not** check that the `DefinitionId` is a content definition: container site keys and corpse keys are registered as "definitions" (`src/World/WorldDelta.cs:788`, `src/World/Runtime/Items.cs:518`).
- One registry per world: `new WorldDelta(Generator, seed, new Registry())` on new game and `new LoadContext(..., new Registry())` on load (`src/Application/GameSession.cs:141,149`); systems reach it as `State.World.Registry`.
- Player-created instances: `WorldDelta.PlaceCreated(hostCell, defId, xCm, zCm)` -> `_registry.CreateEntity(DefinitionId.Parse(defId))` (**inferring overload**) -> `CreatedEntityRecord(InstanceId, DefId, HostCell, XCm, ZCm, BaselineHash?) {Count, Quality}` persisted whole, "a dropped item, a placed chest" (`src/World/WorldDelta.cs:47-61,347-354`).

### 4.4 How to add an EntityKind, and the blast radius (FACT + INFERENCE)

Required edits: (1) enum member in `src/Domain/EntityKind.cs:12-31` (append, INFERENCE: order is irrelevant to saves, see below, but append anyway); (2) a 3-letter lowercase prefix case in `EntityKinds.Prefix` (:35-50) - `TryFromPrefix` picks it up automatically; (3) optionally a case in `TryInferFromDefinition` (:76-83) if the new content kind maps one-to-one onto the instance kind (e.g. `structure` -> `Building`) - this would let `WorldDelta.PlaceCreated` work unchanged, but changes the pinned behaviour tested at `tests/EntityRegistry.Tests/EntityRegistryTests.cs:217-238` only if you change existing mappings; (4) docs: `docs/DATA_MODEL.md:123` prefix list and D-04's wording (`docs/DECISIONS.md:101`).

- **Saves/fixtures**: instance IDs are serialized as strings and re-parsed with `EntityId.Parse` (`src/Persistence/SectionCodec.cs:417-421,562-582`), never as enum ordinals, so adding a kind does not change any existing fixture or require a `schema_version` bump by itself. A *new persisted section/record* (e.g. `buildings.msgpack`, `docs/PERSISTENCE.md:115,136,248-252`) does - `buildings.msgpack` is documented but **not implemented** (no code hits for "buildings" outside presentation).
- Record validators check kinds explicitly (e.g. `record.InstanceId.Kind != EntityKind.Container` / `.Item` / `.Creature`, `src/World/WorldDelta.cs:775,785,805`; `src/World/PlayerState.cs:113`); a new record type needs its own kind check.
- **Architecture tests**: none enumerate `EntityKind`; the registry dependency test and static-state test are unaffected by an enum member (`tests/Architecture.Tests/ArchitectureTests.cs:86-104,196-202`).
- **Determinism** (INFERENCE): `NewId` is wall-clock + crypto-random, so structure/piece instance IDs minted by player commands differ between two replays of the same command log. Precedent for deterministic identity exists (hash-derived `Create(kind, 1, hash(...))` for NPCs/creatures), but it contradicts DATA_MODEL/EntityId wording (§9). M7 must decide which (replay tests vs "registry-generated only").
- DATA_MODEL also lists "the registry's ULID anchor clock" as save-version-sensitive (`docs/DATA_MODEL.md:846`); no such clock exists in code (`NewId` uses `DateTimeOffset.UtcNow`).

---

## 5. How the domain receives a built catalogue (FACT)

- Rule: "Domain never references Application or Presentation, and never reads files to load content - it receives a built catalogue. Content is referenced by string ID." (AGENTS.md architecture rules; `docs/ARCHITECTURE.md`).
- Mechanism: `UNNAMED.Content` (which references Domain and World) turns `ContentLoader` data into **Domain records** (`ItemCatalog`, `ItemDefinition`, `LootTable`, `Merchant`, `NpcDefinition`, `DialogueDefinition`, `QuestDefinition`, `NodeDefinition`, `RecipeDefinition`, `CraftingConstants`, `CompanionTuning`, `RegionLayout`, `MovementRules`, `TierRules`, `ProgressionRules`, `EffectDefinition`, `CreatureDefinition`, `FormulaDefinition`...) and **World setup records** (`ItemSetup`, `CombatSetup`, `MagicSetup`, `CraftingSetup`, `SocialSetup`, `QuestSetup` in `src/World/Runtime/*.cs`), composed into `SimulationSetup(RegionLayout Layout, MovementRules Movement, ProgressionRules Progression, TierRules Tiers, int TickMilliseconds) { Items, Combat, Magic, Crafting, Social, Quests }` (`src/World/Runtime/Simulation.cs:14-33`) by `GameSession.Boot` (`src/Application/GameSession.cs:86-99`).
- `Simulation.Start(SimulationSetup setup, PlayerRecord player, WorldDelta world, long worldTick, IEventBus events)` (`src/World/Runtime/Simulation.cs:148-153`); each system gets `SystemContext{State, Setup, Events, Dispatch}` (`src/World/Runtime/Systems.cs:24-36`).
- Catalogues are immutable (`ImmutableSortedDictionary<string, T>` keyed by definition ID, ordinal) - "Definitions are immutable at runtime" (`docs/DATA_MODEL.md:17` M-6).
- Units are converted at the boundary: metres -> millimetres (`Mm`, round-half-away), seconds -> ticks via `config.time` (`ToTicks`), kg -> grams, degrees -> millidegrees (§7).
- Presentation gets definitions only through Application (`GameSession.Setup`, `DisplayName`) and its own `art_bindings.json` (game IDs -> asset semantic IDs; "nothing here changes a rule, a save or the content hash", `src/Presentation/Art/ArtBindings.cs:28-31`). New piece/faction IDs that need visuals need `art_bindings.json` entries (presentation data), not content fields.
- Architecture test pins the Simulation's public surface to a fixed method list (`tests/Architecture.Tests/ArchitectureTests.cs:112-131`) - new read views for factions/buildings require updating it deliberately.

---

## 6. `content_hash`, `content_version` and what changes them

### 6.1 Computation (FACT, `src/Content/ContentLoader.cs:756-775`)

```
CanonicalHasher: Add("unnamed.content-hash/v1").Add(definitionCount)
  for each (id, envelope) in _definitions ordered by id (Ordinal): Add(id).Add(Normalize(envelope.YamlSource))
  if <contentRoot>/_aliases.yaml exists: Add("_aliases.yaml").Add(Normalize(file text))
  Finish() -> "sha256:<lowercase hex>"
Normalize = TrimStart(BOM) + CRLF->LF
```
`CanonicalHasher` = SHA-256 over length-prefixed UTF-8 strings (4-byte big-endian length) and 8-byte big-endian integers (`src/Domain/CanonicalHasher.cs:15-54`).

### 6.2 What changes it (FACT/INFERENCE)

- Any added/removed/renamed definition (count and IDs are hashed).
- **Any byte of any definition file**, including comments, `notes`, whitespace, key order (raw text is hashed) - except BOM/line-ending differences.
- Any change to `_aliases.yaml` (including comments).
- Root-level `*.yaml` definitions count too (they are in `_definitions`).
- Not hashed: files skipped by the loader (`_`-prefixed other than `_aliases.yaml`, files in unregistered directories), presentation `art_bindings.json`, code.
- Adding faction or structure-piece definitions therefore always changes `content_hash`.

### 6.3 What the hash is used for (FACT)

- Written to every save manifest (`content_hash`, `content_version`) (`docs/PERSISTENCE.md:157-158`; `src/Persistence/SaveStore.cs:54`).
- On load, if the save's `content_hash` differs from the running pack, **every stored definition ID is resolved** through `ContentIdentity` (current / renamed / replaced / discarded / **unresolved = blocker**) (`src/Persistence/SaveLoader.cs:158-160,206-229`; `src/Persistence/ContentIdentity.cs:34-101`). IDs covered today: inventory, cell flags, entity and created-entity `def_id`s, container items, progression IDs, discoveries, effects, creature records, relationships, conversations (and later quest/companion records) (`src/Persistence/SaveLoader.cs:234-330`). INFERENCE: M7's persisted faction IDs, piece `def_id`s and any faction-keyed records must be added to this pass, or removing/renaming a faction/piece definition would silently orphan save data.
- "`content_hash` is exact identity, not a migration trigger for tuning"; content **tuning** that feeds a persisted value is **baseline-locked** and needs an explicit migration step (`docs/PERSISTENCE.md:276,316,324`). INFERENCE for M7: e.g. a piece's `health_max` if current health is persisted as an absolute value, or faction `reputation_tiers` thresholds ("threshold changes retroactively reclassify a player", `docs/DATA_MODEL.md:846`).
- It is **not a generation input** since M2b (`docs/PERSISTENCE.md:58-60,316`; `src/World/Generation.cs:29-33` "Runtime-only content ... never does, which is why a balance edit cannot move the world"). The world baseline is keyed by the generator fingerprint = generator id + worldgen version + RNG contract + `GenerationProfile.Digest` (nodes, populations, terrain, fixed nodes) + probe digest (`src/World/Generation.cs:306-319`). Region `structures`, doors, switches, barriers, stations and containers are **not** in the profile. INFERENCE: authored or player structures do not move baselines unless M7 deliberately adds them to the profile (which would need a registered `BaselineTransition`, cf. `src/Application/GameSession.cs:108-117`).
- `content_version` is a human label (`"0.3.0"`, `src/Application/GameSession.cs:21`), "diagnostic and reporting only" (`docs/PERSISTENCE.md:315`).
- CI guard: the committed historical save fixtures load against their fixture content packs; renaming/removing a referenced ID without an alias fails the build (`docs/DATA_MODEL.md:119`; `tests/Persistence.Tests/Fixtures/README.md:25-35`).

---

## 7. Config YAML conventions for tunable numbers (`content/config/*`)

FACT, from all 13 files (`content/config/*.yaml`) and their builders:

- **One group per file**, `id: config.<group>`, `kind: config`, file `content/config/<group>.yaml`; full envelope (`schema: 1`, `display_key: config.<group>.name`, `tags: [config]`, `notes:`). Groups at e10d2c4: `base_speeds`, `companion`, `crafting`, `creature_behaviour`, `damage_constants`, `economy`, `inventory`, `level_cap`, `magic`, `progression`, `simulation_tiers`, `time`, `xp_curve`.
- "These are DEFINED HERE and nowhere else" for time units (`docs/DATA_MODEL.md:700-712`); AGENTS.md: progression "numbers are content", never hard-coded.
- **Units in key suffixes**, converted at build time: `_m` metres -> integer mm (`Mm`: `Math.Round(x*1000, AwayFromZero)`, `src/Content/CombatContent.cs:473`, `WorldContent.cs:457`); `_s` seconds -> ticks via `ToTicks(seconds, tickMs)` (`CombatContent.cs:423-424`; negative refused) or ms; `_kg` -> grams (`ItemContent.cs:247`); `_deg` -> millidegrees; `_percent` whole numbers 0..100; `_per_s` rates; `_m_s` speeds. Some legacy keys lack suffixes (`weight` kg, `reach` m, `attack_speed` swings/s in items).
- **Nested maps by concern** (`quality:` in crafting, `focus:/strain:/skill:/resonance:/casting:` in magic, `awareness:/noise_m:/corpse:/roles:` in creature_behaviour) and flow-style inline maps for small records (`stagger: { threshold_percent: 14.5, duration_s: 0.6, immunity_s: 2.0 }`, `content/config/damage_constants.yaml:16`).
- **Rationale inline as comments** on each value (D-03 chose YAML for comments; e.g. `content/config/crafting.yaml:8-13`).
- **Tick base**: `config.time.ticks_per_second: 20` must divide 1000 (`src/Content/WorldContent.cs:103-110`); a game day = 57,600 ticks (`content/config/time.yaml:10`).
- **Built into typed, range-checked domain records** that throw with a message naming the group: e.g. `CraftingConstants` (`src/Content/CraftingContent.cs:36-55`), `CompanionTuning` with `.Problem()` (`src/Content/SocialContent.cs:51-62`), `MovementRules`, `TierRules` (`src/Content/WorldContent.cs:56-93`).
- **Presence policy varies by group** (pick one explicitly for M7): optional with code defaults (`config.crafting` absent -> `new CraftingConstants()`, `CraftingContent.cs:38-39`); required only when content needs it (`config.companion` iff an NPC has `companion:`, `SocialContent.cs:42-43`); required when a region exists (`config.base_speeds`, `config.simulation_tiers`, `WorldContent.cs:38-42`); required as a set (`config.time/xp_curve/level_cap/progression`, PRG002, `ProgressionContent.cs:53-60`). Missing required config -> `KeyNotFoundException("<id> is missing (DATA_MODEL.md §4.19)")` (`CombatContent.cs:446-449`).
- **Backward-compatible key additions**: new keys are optional and keep code defaults so older packs (including persistence fixture packs) still build - "a pack from before them keeps the defaults" (`src/Content/WorldContent.cs:64-77`).
- **Derived totals asserted in content**: `config.xp_curve.total_to_level_50_expected: 2448025` is re-summed by the lint (`content/config/xp_curve.yaml:11`).
- Nothing in config is a generation input; but config values that feed persisted derived values are baseline-locked tuning (§6.3).

INFERENCE for M7: a `config.building` (piece placement tolerances, snap grid mm, repair rates) and a `config.factions`/`config.reputation` (tier thresholds, decay) would follow this pattern; thresholds that reclassify persisted standing need a migration policy.

---

## 8. Existing definitions resembling structures, placeables, blockers, doors, factions, groups, reputation

Grep of `content/` and `src/Content` for faction, group, structure, placeable, door, barrier, fence, wall, reputation, standing (FACT):

**Structures / blockers (region-inline, not definitions)**
- `region.ashen_hollow` `structures:` list: `{ id, box_m: [min_x, min_z, max_x, max_z] | circle_m: [x, z, r], height_m, clearance_m? }` - 44 structures at M3 (`docs/M3_STATUS.md:13`), e.g. `rock_waystone`, `longhouse_north|south|west|east_north|east_south` (walls as thin boxes, 0.4 m thick, 3.2 m tall), `forge_*` (3.0 m), `fence_smithy` (1.2 m), trees `tree_1..32`, a fallen beam overhang with `clearance_m` (`content/regions/ashen_hollow.yaml:63-140`). Built into `WalkSpace(minX, minZ, maxX, maxZ, terrain, structures)` (`src/Content/WorldContent.cs:146-148,292`).
- Buildings are therefore **wall-segment blockers with a door gap**, not building definitions; there is no roof/floor in the domain (INFERENCE: roofs exist only as presentation art, cf. untracked `docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md` §24 "Roof correction", untracked, authority unconfirmed).
- `base_speeds` tuning is explicitly calibrated against these heights: jump apex 1.15 m "under the fence's 1.2 m" (`content/config/base_speeds.yaml:12-17`).

**Doors** - `doors: [{ key: door.longhouse, flag_ref: world.hollow.longhouse_door_open, box_m, height_m: 2.4 }, { key: door.forge_shed, ... }]` (`content/regions/ashen_hollow.yaml:142-144`); `DoorSite(Key, FlagId, BoxBlocker ClosedFootprint)` (`src/Domain/Spatial/RegionLayout.cs:12`); WLD004 (`src/Content/WorldContent.cs:151-164`). Flags: `world.hollow.longhouse_door_open`, `world.hollow.forge_shed_door_open` (`type: bool`, `default: 0`, `owning_system: S-22`, `content/world_flags/hollow/*.yaml`).

**Barriers / switches (M6)** - `barrier.foldscar_fold` (`circle_m: [145, 42, 3]`, `height_m: 2.6`, `prompt`), four `switch.*` on rock structures setting `world.foldscar.*` flags with `requires` (`content/regions/ashen_hollow.yaml:164-199`); `SwitchSite`, `BarrierSite` (`src/Domain/Spatial/RegionLayout.cs:20-28`); `RegionLayout.ClosedDoors(isOpen, isLifted)` returns the blockers that currently stand (:94-97).

**Containers / stations (placeable-like)** - `containers: [{ key: container.den_cache, loot_ref, position_m, stack_slots }]`, `stations: [{ key: station.forge_hearth, kind: forge, position_m }, { key: station.forge_anvil, kind: anvil, ... }]` (`content/regions/ashen_hollow.yaml:146-163`); `ContainerSite`, `StationSite` (`src/Domain/Spatial/RegionLayout.cs:37,46`). No "placeable" or "piece" concept anywhere in code or content (grep: none).

**Factions / groups / reputation / standing** - no content; only the registered-but-empty `faction` kind, `faction_ref` suffix, the tag-legacy `faction.` prefix, QuestContent's refusal, `reputation` as an unbuilt reward kind, and the unbuilt `faction_reputation`/`faction_state` objective types (all cited in §3.3). "group" and "standing" appear only in prose/comments (grep). Creature `roles` in `config.creature_behaviour` (`den_guardian`, `pack_hunter`, `calls_for_help`, `answers_calls`) are the only "group behaviour" data (`content/config/creature_behaviour.yaml:23-32`) - a pack is a spawner, not a faction.

**Walls in docs** - `docs/DATA_MODEL.md:148`: "Building piece | sockets, material cost, health, nav footprint, station capability | transform, owner, current health, repair state, attached storage, occupant assignment"; `:150`: "Faction | membership graph, attitude defaults, service gates, territory | player reputation value/tier, at-war flags, crime records, bounties". PERSISTENCE §5.4 building/piece identity (`docs/PERSISTENCE.md:248-252`), WORLD_ARCHITECTURE §10 (`docs/WORLD_ARCHITECTURE.md:403-418`).

---

## 9. Contradictions found

### 9.1 DATA_MODEL vs code (validator claims not implemented)

1. "Suffix rule: the ID's segments must match the file stem and the ID prefix must match `kind` ... The validator enforces this" (`docs/DATA_MODEL.md:78`) vs code: no stem or prefix check anywhere in `src/Content` (only an unused CLI extension `MatchesId`, `src/Content/Cli/ContentCliApp.cs:176-185`). PROBE: `id: npc.test.not_a_faction`, `kind: faction`, in `factions/whatever.yaml` lints clean.
2. "`world_flag` uses the `world.` prefix ... The validator enforces the prefix" (`docs/DATA_MODEL.md:70`) vs code: not enforced. PROBE: `id: flag.test.wrong_prefix`, `kind: world_flag` lints clean.
3. Kind/directory agreement: `ValidateKindDirectoryMatch` compares the file's first path segment with the directory being scanned, never with the declared kind's directory (`src/Content/ContentLoader.cs:439-495`; `actualDir` is computed at :448 but only null-checked). PROBE: a `kind: faction` file inside `npcs/` loads as a faction with 0 errors. (DATA_MODEL's bijection, `:36`.)
4. "A directory with no `kind` and a `kind` with no directory are both validator errors" (`docs/DATA_MODEL.md:36`) vs code: a registered kind with no directory is silently skipped (`src/Content/ContentLoader.cs:119-123`); an unknown directory is an error that does not fail `LoadAll` (PROBE: CLI exit 0).
5. "tags from `content/_tags.yaml` (validator-enforced)" (`docs/DATA_MODEL.md:32,87`; `src/Content/ContentDefinition.cs:46,85,131-133`) vs: no `_tags.yaml` exists and tags are unchecked (TODO at `src/Content/ContentLoader.cs:651`).
6. "Content files never contain instance IDs - a ULID-shaped value in YAML is a validation error" (`docs/DATA_MODEL.md:123`) vs: no such check. PROBE: `leader: npc_01ARZ3NDEKTSV4RRFFQ69G5FAV` lints clean.
7. Envelope completeness is in the mandatory validation pass (`docs/DATA_MODEL.md:827`) vs: missing `display_key`/`tags`/`schema` are accepted (PROBE).
8. Definition `schema:` "Loader rejects the file" on mismatch (`docs/DATA_MODEL.md:839` table row) vs: `schema` is never parsed (INFERENCE §1.4; PROBE `schema: 99` accepted).
9. M-5 "hard error with file and line" (`docs/DATA_MODEL.md:16`) vs: builder lints (`ITM001` `src/Content/ItemContent.cs:214`, `CMB001` `CombatContent.cs:442`, `MAG001` `MagicContent.cs:184`, `CRF001` `CraftingContent.cs:154`, `SOC001` `SocialContent.cs:319`, `QST001` `QuestContent.cs:380`) carry neither file nor line.
10. "duplicate IDs (hard error naming both files)" (`docs/DATA_MODEL.md:827`) vs: `DUP001` names only the later file and prints the literal `<original>` (`src/Content/ContentLoader.cs:350-356`).
11. Tooling "`content-schema-dump` (JSON Schema per kind)", "`content-id-graph`", "`content-aliases`" (`docs/DATA_MODEL.md:829`) vs: `schema-dump` prints the kind table only; `xref` is a per-kind listing; no aliases tool (`src/Content/Program.cs:41-58`).
12. "shipped builds use the compiled cache" / hot-reload (`docs/DATA_MODEL.md:42`; D-03 `docs/DECISIONS.md:89-90`) vs: YAML is parsed at every boot; no cache exists.
13. Validation also claims "alias chains resolve in one hop with no cycles" (`docs/DATA_MODEL.md:827`) vs `ValidateAliasTargets` accepts a target that is itself an alias key (`src/Content/IDValidation.cs:265-278`), i.e. multi-hop chains are allowed; cycles are detected only at save-load resolution (`src/Persistence/ContentIdentity.cs:94-95`). DATA_MODEL §2.1 itself says "Renames may chain; a cycle is an error" (`:117`) - internal doc inconsistency.

### 9.2 Identity docs vs code

14. "Generated **only** by the Entity Registry (S-02) at creation" (`docs/DATA_MODEL.md:123`), M-4 "Runtime instances get ULID instance IDs from the Entity Registry" (`:15`), and "Instance IDs are runtime identity only and are never derived from a seed" (`src/Domain/EntityId.cs:17-20`) vs: NPC and creature IDs are hash-derived outside the registry and then registered with `CreateEntity(defId, explicitId)` (`src/World/Runtime/Social.cs:121-125,134-135`; `src/World/Runtime/Creatures.cs:162-166`). Documented locally as "Identity is derived" (`docs/M4_STATUS.md:56`). Not a seed, but not registry-generated either.
15. `EntityId.Create` doc "For fixtures and tests; runtime code uses NewId" (`src/Domain/EntityId.cs:55-57`) vs the runtime uses above.
16. DATA_MODEL "the registry's ULID anchor clock" is save-version-sensitive (`docs/DATA_MODEL.md:846`) vs: no such clock; `NewId` uses wall-clock UTC.

### 9.3 Docs vs owner rulings (only those touching this topic; flag for the synthesis)

17. **Ruling 2 (one storey, snap philosophy)** vs `docs/ROADMAP.md:283` "foundations, walls, floors, roofs, doors; **free rotation** and socketing" and D-08 "free rotation/socketing" (`docs/DECISIONS.md:182`). "Floors/roofs" are compatible with one storey only if floor = ground floor (INFERENCE); "free rotation" also conflicts with the domain's axis-aligned `BoxBlocker` (`src/Domain/Spatial/Blockers.cs:40`) unless a rotated blocker is added.
18. **Ruling 1 (Godot Navigation not authoritative; domain-derived nav)** vs wording "the navmesh updates on placement" (`docs/ROADMAP.md:284`) and "Navmesh around buildings is rebuilt per cell on change, debounced" (`docs/WORLD_ARCHITECTURE.md:414`). Not necessarily a contradiction, but "navmesh" must be read as domain nav data derived from authoritative structure state, not Godot's `NavigationServer`.
19. **Ruling 4 (C10 retired)** is recorded in tracked docs: `docs/PROTOTYPE.md:298` ("Revised by owner ruling (2026-09-24) ... No single encounter is required to force every combat tool"), `docs/M6_STATUS.md:105,141,196`. The untracked `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md` §18 (lines ~609-633) still calls the same direction "Recommended design direction (not yet formally owner-ratified ...)" - stale relative to the ruling (untracked, authority unconfirmed).
20. **Ruling 3 (keep relationship/reputation/legal/faction/war/identity/threat/legality separate; no global morality meter; info not magically global)** is consistent with `docs/SYSTEMS.md:308` S-27 ("It never decides who attacks: tactical hostility and attack legality are derived from faction relation, war state, legal status, identity knowledge and perception, not from a standing tier") and ROADMAP M7 "no universal morality meter" (`docs/ROADMAP.md:282`), and with `Relationships` "there is no single good-or-evil meter" (`src/Domain/Social/Social.cs:25-28`). The explicit "keep separate" list and "no psychic faction omniscience" are recorded in the untracked V4 handoff §40 and §13 (`OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md` ~1402-1414, ~459; untracked, authority unconfirmed). DATA_MODEL §4.13's `attitude_default` single scalar per faction pair (`docs/DATA_MODEL.md:560`) and `laws.response: guards_hostile` (`:575-576`) couple attitude/legal response to hostility more tightly than the ruling allows - flag for the factions researcher.
21. **Ruling 5 (no core action may require a radial)**: recorded in the untracked V4 handoff §3 (`~:78-82`: "Radial menus are optional convenience only; no core action, spell, weapon, companion command, building function, emote or interaction may require a radial"). Nothing in content/registry conflicts.
22. **Ruling 6 (no networking)**: `docs/ROADMAP.md:20` R-6 and `:63` ("any networking before M15"), `docs/IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md` §6 "networking ... Preserve seams only". Consistent.
23. **Ruling 7 (engine-independent authority; dotted IDs; ULIDs; sparse deltas)**: consistent with D-02/D-04/D-05/D-10 and code, modulo items 14-16 above and the site-key registrations in §2.3.

### 9.4 Doc vs doc

24. `DATA_MODEL.md` §1 kind table calls `merchant` a main-table kind but its note says it is "a kind without a `merchants/` resolver target in §5's table" (`docs/DATA_MODEL.md:74`), while §5's table does list `merchant_ref` -> `merchant` (`:818`) and code resolves it (`src/Content/ContentChecks.cs:41`). The note is stale.
25. `faction` example territory uses `cell.stoneford_town` (`docs/DATA_MODEL.md:573`) although `cell` is not a kind and cell keys are `r_X_Y:c_XX_YY` strings (`content/regions/ashen_hollow.yaml:8`).

---

## 10. Where the owner rulings are recorded (limited to what this topic's reading surfaced)

| Ruling | Recorded (tracked @e10d2c4) | Recorded (untracked) | Conflicting text |
|---|---|---|---|
| 1 Navigation domain-authoritative | not found as an explicit ruling in the files read | not found in V4 headings read | ROADMAP :284 / WORLD_ARCHITECTURE :414 "navmesh" wording (ambiguous) |
| 2 One storey, snap | not found in files read | not found | ROADMAP :283 "floors, roofs ... free rotation"; D-08 :182 "free rotation" |
| 3 Factions separation | S-27 (`docs/SYSTEMS.md:302-308`), ROADMAP :282 "no universal morality meter" | V4 §40, §13 | DATA_MODEL §4.13 attitude/laws shape (partial) |
| 4 C10 retired | `docs/PROTOTYPE.md:298`, `docs/M6_STATUS.md:105,141,196` | V4 §18 still "not yet formally owner-ratified" | V4 §18 (stale) |
| 5 No required radial | not found in tracked files read | V4 §3 :78-82 | none |
| 6 No networking | ROADMAP R-6 :20, :63; IMPLEMENTATION_PRECEDENCE §6 | - | none |
| 7 Authority/IDs/deltas | D-02, D-04, D-05, D-10; DATA_MODEL M-1..M-7 | - | identity items 14-16 |

(Other researchers own navigation/building/faction design and may find rulings 1, 2 and 5 recorded elsewhere; this table only reports what the content-registry reading encountered.)

---

## 11. Concrete checklist distilled for M7 content work (INFERENCE)

1. Factions: `content/factions/<scope>/<name>.yaml`, IDs `faction.<scope>.<name>`; new `FactionContent` builder + closed field set; decide which DATA_MODEL §4.13 fields are built vs refused; lift QuestContent's `faction_ref` refusal only if quests use it; decide whether NPC/creature `faction_ref` is built (today silently ignored); add persisted faction IDs to the SaveLoader definition-ID pass.
2. Structure pieces: register a kind (e.g. `structure`, dir `structures`), add `structure_ref` (and sub-kinds) to `ReferenceSuffixes`; piece schema with footprint in `box_m`/`circle_m` metres (+ 90-degree rotation policy), `height_m`, `clearance_m`, socket list, material cost (`inputs: [{item_ref, count}]` reuses recipe shape and is auto-xref'd), `health_max`, optional `station: <kind string>`; closed field set; `STR001`-style lint + line-accurate SEM checks if desired.
3. Instances: use `EntityKind.Building` (`bld`) for structures; add a piece prefix (or document that pieces are sub-rows of a `bld_` structure); pass `EntityKind` explicitly or extend `TryInferFromDefinition`; persist `def_id` strings so the definition-ID pass can remap them; decide deterministic vs random ULIDs for replay.
4. Tighten the linter generically (cheap, high value before content scale): prefix = kind `IdPrefix`, stem = last segment(s), kind directory = scanned directory, `DIR003` clears `success`, envelope completeness, ULID-shaped values rejected. Expect to repair fixture content packs if they violate new rules (precedent `3317e3d`).
5. Update `tests/Content.Tests/ValidationTests.cs:516-546` with every new definition ID.

---

## 12. Open questions

- Should M7 finally enforce the DATA_MODEL "suffix rule" (prefix/stem/directory agreement) before adding two new content families, and accept the resulting fixture-pack repairs?
- Kind naming for pieces: one `structure` kind with a `piece_type` enum, or sub-kinds (`structure.wall`, ...)? Sub-kinds need explicit listing in every `*_ref` target set.
- Instance prefix for pieces: new 3-letter prefix (DATA_MODEL/D-04 list update) or pieces as non-entity rows inside a `bld_` structure record (PERSISTENCE §5.4 says pieces are "ULID-keyed rows")?
- Deterministic identity for command-created structures (hash of command/tick) vs `NewId` (random): which does replay/determinism testing require, and does the owner accept the DATA_MODEL wording change?
- Do authored settlement buildings (longhouse, forge shed, fence) migrate from region-inline blockers to piece-based structures in M7? That changes `art_bindings.json` keys and WLD lint, but not the worldgen baseline.
- Where does faction-wide state live, given world flags are cell-scoped? (Recommended: an owned S-27 slice, not flags.)
- Does NPC `faction_ref` become required, optional, or remain ignored? Today it is silently accepted (PROBE).
- DATA_MODEL §4.13's `attitude_default` scalar `[-1,1]` and `laws.response` values: keep, or replace to honour ruling 3's separation of attitude, legal status and attack legality?
- Should builder-style lints gain file paths (at minimum `definition.SourceFile`) so M7's faction/piece errors meet M-5?
