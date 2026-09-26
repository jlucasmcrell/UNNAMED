# sim_core — the authoritative simulation architecture as built (origin/main @ e10d2c4)

Research notes for the M7 design effort (Factions, Reputation, and Building v1). Read-only research. Every citation is
`repo-relative/path:line` at commit e10d2c4 unless marked otherwise. **FACT** = read in the cited source. **INFERENCE** = my
reading, not stated anywhere. Untracked docs in `G:/UNNAMED/docs` are labelled "untracked, authority unconfirmed".

---

## 0. The ten things a designer must know first

1. **There are two "system" architectures in the repo, and only one runs the game.** The M1 scaffold (`ISystem`, `IWorldState`,
   `IWorldStateWriter`, `ICommandBus`, `SimulationContext` in `src/Domain`; `CommandBus`, `TickScheduler` in `src/Application`)
   is exercised only by `tests/Domain.Tests`. The live game runs on `src/World/Runtime`: `Simulation` + `RuntimeState` +
   `StateSlice`/`SliceOwner` + `SystemContext` + `GameCommand`/`InternalCommand`. **Specify M7 against the live runtime.**
   FACT: `docs/M3_STATUS.md:45` ("Where the runtime systems live: `src/World/Runtime`, not `src/Domain` ... M1's `ISystem` needs
   Domain internals, so a World system cannot implement it"); `src/Application/TickScheduler.cs:41-50` (`RunTick` is an empty
   placeholder); grep shows no runtime type implements `ISystem` (only `tests/Domain.Tests/PickUpItemDemo.cs:33`).
2. **One fixed tick = 50 ms (20 Hz)** from `content/config/time.yaml:7` (`ticks_per_second: 20`), converted by
   `src/Content/WorldContent.cs:103-110` (must divide 1000 exactly). `world_tick` is a `long` counting *completed* ticks.
3. **Commands are FIFO, applied only at tick boundaries** by `Simulation.DrainCommands` (`src/World/Runtime/Simulation.cs:268-306`),
   dispatched by a C# `switch` on the command type — not by system order. Every command, accepted or rejected, is logged.
4. **The tick order is a hard-coded list** in `Simulation.Step` (`src/World/Runtime/Simulation.cs:312-339`): movement, tiers, tier
   B/C stubs, combat, creatures, companions, NPCs, dialogue, effects, death, discovery, quests, clock.
5. **Systems never subscribe to events.** Cross-system work is a synchronous, re-entrant internal command
   (`SystemContext.Dispatch`, `src/World/Runtime/Systems.cs:39`) routed by `Simulation.Dispatch` (`Simulation.cs:369-404`). Events
   go only to presentation/tests, synchronously and immediately (not queued to end of tick).
6. **Every write goes through a `RuntimeState` method that checks a `SliceOwner` token**; a slice claimed twice or never claimed is
   a boot error (`src/World/Runtime/RuntimeState.cs:144-161, 329-333`).
7. **Positions are integer millimetres (`long`)**; facing is integer millidegrees in [0, 360000) measured from +Z towards +X
   (`src/Domain/Spatial/Kinematics.cs:30-34`). World-delta records for dropped items store **centimetres within the host cell**
   (`int XCm/ZCm` in [0, 10000)) (`src/World/WorldDelta.cs:54`). Cells are 100 m, regions 2 km (20x20 cells)
   (`src/World/Coordinates.cs:15-18`).
8. **The world delta (`WorldDelta`) persists five kinds of divergence today**: per-cell world flags, per-cell node harvest records,
   per-cell population counts + slot-occupant records (built but unused by the game), created instances (dropped item stacks),
   changed containers (authored chests, corpses, trader wares), and creature records. Every record carries its host cell's
   `baseline_hash`. There is **no building, faction, or structure record** anywhere in code.
9. **Randomness is `RngChannel.Open(worldSeed, cell, subsystem, semanticKey)` + sample index** — counter-based SplitMix64 over a
   SHA-256 channel seed (`src/World/StableRandom.cs:38-85`). No `System.Random` in src/Domain, src/World or src/Application.
   Instance IDs (`EntityId.NewId`) use wall clock + crypto RNG and are **not** deterministic; NPC and creature IDs are deliberately
   derived by hash instead.
10. **Save/replay proof** is `Simulation.StateDigest()` (`Simulation.cs:357-364`) and `StateDump.Render/Compare`
    (`src/Application/StateDump.cs:25-90`), which field-compares the whole player record + the whole world delta (M6: "415 fields
    compared, 0 differences", `docs/M6_STATUS.md:172`). The command log is **kept in memory only**; `command_log.jsonl` is not
    written (`src/Persistence/SaveStore.cs:474`, `docs/M3_STATUS.md:52`), contradicting `PERSISTENCE.md` §5.7.

---

## 1. Assemblies and dependency direction (as built)

| Assembly (`src/<X>`) | References | Holds | Notes |
|---|---|---|---|
| `Domain` | nothing project-level | IDs (`EntityId`, `EntityKind`, `DefinitionId`), `CanonicalHasher`, the M1 scaffold interfaces, **pure rules** per feature (`Domain/Spatial`, `Combat`, `Creatures`, `Crafting`, `Items`, `Magic`, `Progression`, `Quests`, `Social`, `Companions`) | `InternalsVisibleTo Domain.Tests` only (`src/Domain/Domain.csproj`). |
| `EntityRegistry` | Domain only | `EntityRegistry` (identity/lifetime) | Asserted by `tests/Architecture.Tests/ArchitectureTests.cs:195-202`. |
| `World` | Domain, EntityRegistry | Coordinates, generation, `RngChannel`, `WorldDelta`, `PlayerRecord`, **the whole runtime** (`World/Runtime`) | `InternalsVisibleTo World.Tests, Persistence.Tests, M2.Probe` (`src/World/World.csproj`). |
| `Content` | Domain, World | YAML loader, validators, `*Content.Build(...)` builders that produce `World.Runtime` setup records | `src/Content/Content.csproj`. |
| `Persistence` | World (and below) | `SaveStore`, `SaveLoader`, `SectionCodec`, migrations, `BaselineTransition` | |
| `Application` | Content, Persistence, World, ... | `GameSession` (composition of content+save+sim, the frame loop), `EventBus`, `StateDump`, M1 `CommandBus`/`TickScheduler` | |
| `Presentation` | Application etc. + Godot | Views, input → commands, prediction | Only project allowed to reference Godot. |

FACT: the rule "only Presentation may reference Godot" is enforced for Domain, Application, Content, EntityRegistry, World,
Persistence (`tests/Architecture.Tests/ArchitectureTests.cs:20-30`).

INFERENCE: the "authoritative domain" in practice is **Domain (pure rules) + World (state + systems)**. New M7 pure rules belong in
`src/Domain/<Feature>/` (static functions over immutable records, like `CraftingRules`, `QuestRules`, `ProgressionEngine`), new
runtime systems in `src/World/Runtime/<Feature>.cs`, new content builders in `src/Content/<Feature>Content.cs`.

---

## 2. Tick loop and command queue semantics

### 2.1 The frame loop (`GameSession.Frame`)

FACT, `src/Application/GameSession.cs:177-201`:

```
elapsed = clamp(realSeconds, 0, MaxFrameSeconds=0.25)        // GameSession.cs:52, 180
accumulator += elapsed; PlaytimeSeconds += elapsed
simulation.DrainCommands()                                     // commands submitted this frame apply now, at the boundary
while accumulator >= TickSeconds (0.05):
    simulation.Step()                                          // one fixed tick
    accumulator -= TickSeconds
    simulation.DrainCommands()   // "anything an event handler queued applies at the next boundary"   (:190)
autosave if AutosaveCadence.IsDue (300 s of playtime)          // SaveStore.cs:39-46
return FrameResult(ticksRun, alpha = accumulator/TickSeconds, autosavedTo)
```

- At most 5 ticks per frame (0.25 s clamp); a stall is clamped, "not replayed" (`GameSession.cs:51-52`; test
  `ALongStall_IsClamped_NotReplayed`, `tests/Application.Tests/SessionTests.cs:88`).
- Presentation calls `_session.Frame(...)` once per Godot frame; scripted modes (smoke, playthrough, delta-shots) pass exactly
  `TickSeconds`, i.e. one tick per frame (`src/Presentation/Main.cs:305`).
- `GameSession.Submit(GameCommand)` → `Simulation.Enqueue` (`GameSession.cs:169-170`, `Simulation.cs:265`). Nothing changes until
  the next `DrainCommands`. "Commands apply at the next tick boundary in the same frame" (`docs/M3_STATUS.md:51`).

### 2.2 `Simulation.DrainCommands` (the only way a GameCommand applies)

FACT, `src/World/Runtime/Simulation.cs:268-306`:
- `while (_queue.TryDequeue(out var command))` — strict FIFO of submission order (`Queue<GameCommand>`, `Simulation.cs:72`).
- A `switch` on the concrete type calls the owning system's `Handle(cmd[, WorldTick])`; each returns `string?` — `null` = accepted,
  otherwise the rejection reason (free text, e.g. `"unknown actor ..."`, `"that is out of reach"`).
- Unknown type → rejected with `"no system handles <Type>"` (`:298`). It is not an exception.
- Every command is appended to `_log` as `LoggedCommand(long Tick, long Sequence, GameCommand Command, string? RejectedReason)`
  (`src/World/Runtime/Commands.cs:29`, `Simulation.cs:300`). `Tick` = `WorldTick` (completed ticks) at the boundary; `Sequence`
  is a monotonically increasing `long` per simulation instance (`_sequence++`).
- A rejection publishes `CommandRejected(GameCommand Command, string Reason, long Tick)` (`Events.cs:13`, `Simulation.cs:301-302`).
- The 23 GameCommands routed today (`Simulation.cs:275-297`): `MoveCommand`, `JumpCommand`, `CrouchCommand`, `TakeAllCommand`,
  `SpendAttributeCommand`, `InteractCommand`, `MoveItemCommand`, `EquipCommand`, `UnequipCommand`, `AttackCommand`, `BlockCommand`,
  `DodgeCommand`, `CastCommand`, `GatherCommand`, `CraftCommand`, `UseItemCommand`, `TalkCommand`, `ChooseCommand`, `LeaveCommand`,
  `BuyCommand`, `SellCommand`, `OrderCompanionCommand`, `ReviveCommand`.
- **Re-entrancy of the queue (INFERENCE from code):** if an event handler enqueues a command *during* a drain, the `while` loop
  dequeues it in the same drain, at the same boundary tick. If it enqueues during `Step`, it waits for the next drain (after that
  step). `DrainCommands` has no re-entrancy guard; presentation source is forbidden to call `.DrainCommands()` or `.Step()`
  (`ArchitectureTests.cs:166-170`).

### 2.3 `Simulation.Step` (one fixed tick)

FACT, `src/World/Runtime/Simulation.cs:312-339`:

```
if (_stepping) throw "Step is not re-entrant: an event handler may enqueue commands, never step the world"
tick = WorldTick + 1
_movement.Tick(tick)        // integrates the player's MoveIntent with Kinematics.Step
_tiers.Tick(tick)           // cell tiers from distance, one step per tick with hysteresis
foreach tierSimulation (B, C stubs): Tick(tick, cells at that tier)
_combat.Tick(tick)
_creatures.Tick(tick)
_companions.Tick(tick)
_npcs.Tick(tick)
_dialogue.Tick(tick)
_effects.Tick(tick)
_death.Tick(tick)           // settles a player death "at the end of the tick it happened in"
_discovery.Tick(tick)
_quests.Tick(tick)          // "which read what all of that did"
_clock.Tick()               // WorldTick++  (the only writer of the Clock slice)
```

- `InteractionSystem`, `WorldFlagSystem`, `ProgressionSystem`, `InventorySystem`, `EquipmentSystem`, `GatheringSystem`,
  `CraftingSystem`, `RelationshipSystem`, `TradeSystem` have **no Tick**: they only react to commands.
- The world clock advances **last**, so everything inside step N+1 reads `WorldTick == N` and is handed `tick = N+1`.

### 2.4 Which tick number a mutation "belongs to"

FACT, `Simulation.cs:366-367`: `private long Now => _stepping ? WorldTick + 1 : WorldTick;` — internal commands dispatched during a
step are stamped with the tick being stepped; during a drain, with the boundary tick. GameCommand handlers receive `WorldTick`
(`Simulation.cs:276-297`). Consequence (INFERENCE): events from "step N" and from "commands applied right after step N" both carry
`Tick = N`; commands applied at boundary N take physical effect in step N+1 (e.g. a `MoveCommand` changes `Intent`, which
`MovementSystem.Tick(N+1)` integrates).

### 2.5 Events

- Events are `public sealed record`s in `namespace UNNAMED.World.Runtime`, past tense, with a trailing `long Tick`
  (`src/World/Runtime/Events.cs:10-11`: "Events are past tense, immutable, and never persisted. Views subscribe to them; nothing a
  view does with one reaches the simulation, which accepts only commands. Tick is the world tick the change belongs to.").
- Published via `_context.Events.Publish(new X(...))` **inline, immediately after the mutation**, from inside a handler or a `Tick`.
  The bus is `UNNAMED.Application.EventBus` (`src/Application/EventBus.cs:15-61`): `ConcurrentDictionary<Type, object>` of
  `List<Action<T>>`; `Publish<T>` looks up exactly `typeof(T)` and invokes handlers **in subscription order** (`:50-60`).
- **Handler ordering never depends on dictionary/hash iteration** (FACT: the dictionary is only indexed by type, never enumerated;
  the per-type list is ordered). No polymorphic dispatch: subscribing to a base type receives nothing.
- **No system subscribes** (FACT: grep for `Subscribe` in `src/World`, `src/Domain`, `src/Application` finds only the bus and
  `GameSession`'s pass-through `IDomainEvents`, `GameSession.cs:35-40, 203-205`). Presentation has ~108 subscriptions
  (`src/Presentation/Audio/SoundEvents.cs:158...`).
- INFERENCE (caveats a designer should know): (a) a subscriber that `Subscribe`s/`Unsubscribe`s the *same* event type from inside
  its handler mutates the `List` being enumerated → `InvalidOperationException`; (b) handlers run mid-tick, so a view can observe a
  half-applied tick (e.g. `BodyMoved` is published by movement before creatures move); (c) an exception in a subscriber propagates
  into the simulation call stack.
- Event count today: ~75 public event records (e.g. `BodyMoved`, `WorldFlagChanged`, `DoorToggled`, `SwitchSet`,
  `LocationDiscovered`, `ExperienceGained`, `CellTierChanged`, `ItemMoved`, `NodeGathered`, `ItemCrafted`, `CreatureKilled`,
  `CreatureNoticed`, `RelationshipChanged`, `QuestStarted`, `ObjectiveSatisfied`, `CompanionCaughtUp`, `PlayerDied`, ...).
- Saving publishes nothing and loading publishes nothing (test `SavingIsNotAnEvent_AndLoadingPublishesNothing`,
  `tests/Application.Tests/DeterminismAndViewTests.cs:128-143`). `TierSystem.Settle()` sets tiers at a hard boundary "with no
  events" (`src/World/Runtime/Systems.cs:470-480`).

### 2.6 Rejection doctrine

Rejection is a normal return value, never an exception (`Commands.cs:9-12`; test `ARejectedCommand_IsAnEvent_WithAReason_NeverAnException`,
`SessionTests.cs:130-148`). Validation pattern everywhere: `if (command.Actor != _player) return $"unknown actor {command.Actor}";`
then domain checks, then mutate. **All GameCommands today are player commands** (FACT: every handler checks
`command.Actor != _player`); NPC/creature/companion actions are system-internal logic, not GameCommands.

---

## 3. How a system is declared, registered and allowed to write

### 3.1 The live pattern (World.Runtime)

| Piece | Where | What |
|---|---|---|
| `public enum StateSlice` | `src/World/Runtime/RuntimeState.cs:19-74` | The closed list of authoritative runtime slices (17 today). Doc comment on each says saved vs transient. |
| `internal sealed class SliceOwner(string System, ImmutableHashSet<StateSlice> Slices)` | `RuntimeState.cs:77-87` | "A system's proof of which slices it owns. Only composition creates one." |
| `internal sealed class RuntimeState` | `RuntimeState.cs:93-334` | The mutable store. Public getters for every slice; one setter method per mutation, each starting `Require(owner, StateSlice.X)`. "It holds records and makes no decisions". |
| `RuntimeState.Claim(string system, params StateSlice[] slices)` | `:144-153` | Throws `"State slice {slice} is claimed by both {owner} and {system}"`. |
| `RuntimeState.RequireEverySliceOwned()` | `:156-161` | Boot error `"State slices without an owning system: ..."`. |
| `Require(SliceOwner owner, StateSlice slice)` | `:329-333` | Throws `"{owner.System} wrote {slice}, which {owner} owns"`. |
| `internal sealed class SystemContext` | `src/World/Runtime/Systems.cs:24-87` | What every system shares: `State` (RuntimeState), `Setup` (SimulationSetup), `Events` (IEventBus), `Dispatch` (`Func<InternalCommand, string?>`), plus shared read helpers (`IsOpen(door)`, `IsSet(switch)`, `IsLifted(barrier)`, `ClosedDoors()`, `FindContainer`, `MerchantSites`, `CorpseSites`, `Obstacles()`, `DistanceToPlayer`, `TalkReachMm`). |
| `internal abstract record InternalCommand` | `Systems.cs:15` | "A command one system sends another. Presentation cannot construct these." |
| `public abstract record GameCommand(EntityId Actor)` | `src/World/Runtime/Commands.cs:14` | Presentation's intents. Reference-type records (not structs). |
| `public sealed record SimulationSetup(RegionLayout Layout, MovementRules Movement, ProgressionRules Progression, TierRules Tiers, int TickMilliseconds)` + init properties `Items`, `Combat`, `Magic`, `Crafting`, `Social`, `Quests`, each defaulting to `XSetup.Empty` | `Simulation.cs:14-33` | Immutable rules built from content at boot. |

A system is a plain `internal sealed class` with a constructor `(SystemContext context, SliceOwner owner, ...extra)`; it keeps
`_context` and `_owner`; exposes `public string? Handle(<Command> c[, long tick])` per command it owns, optionally
`public void Tick(long tick)`, and read-only `Views()` for presentation. Its header comment states `Owns: <see cref="StateSlice.X"/>`
or `Owns no state` (convention required by `docs/ARCHITECTURE.md:143` and followed in every runtime file).

### 3.2 The composition root

FACT, `src/World/Runtime/Simulation.cs:100-145` (the private constructor): builds `RuntimeState`, `SystemContext(_state, setup,
events, Dispatch)`, registers carried items with the registry, then constructs every system **in this explicit order** with its
claim, e.g. `_clock = new ClockSystem(_context, _state.Claim(nameof(ClockSystem), StateSlice.Clock));` (`:116`). Then
`_state.RequireEverySliceOwned()` (`:139`), then population/seeding: `_effects.Seed(...)`, `_npcs.Populate()`,
`_companions.Populate()`, `_creatures.Populate()`, `_tiers.Settle()` (`:140-144`).

Current ownership (FACT, `Simulation.cs:116-137` + file headers):

| System (file) | Slices claimed | Persisted where | Tick? |
|---|---|---|---|
| `ClockSystem` (Systems.cs:90) | `Clock` | manifest `world_tick` | yes (last) |
| `MovementSystem` (Systems.cs:109) | `PlayerBody` (body + posture) | player.msgpack | yes (first) |
| `InteractionSystem` (Systems.cs:240) | none — dispatches `SetWorldFlag` | — | no |
| `WorldFlagSystem` (Systems.cs:296) | `WorldFlags` | cells.msgpack `flags` | no |
| `ProgressionSystem` (Systems.cs:325) | `PlayerProgression` | player.msgpack | no |
| `DiscoverySystem` (Systems.cs:420) | `Discoveries` | player.msgpack | yes |
| `TierSystem` (Systems.cs:457) | `CellTiers` (transient) | not saved | yes |
| `InventorySystem` (Items.cs:114) | `PlayerInventory`, `WorldItems` | player.msgpack; entities.msgpack `created`, `containers` | no |
| `EquipmentSystem` (Items.cs:689) | `PlayerEquipment` | player.msgpack | no |
| `CombatSystem` (Combat.cs:300) | `Combat` (player's; transient) | not saved | yes |
| `CreatureSystem` (Creatures.cs:130) | `Creatures` (working set + world-delta creature records) | entities.msgpack `creatures` | yes |
| `StatusEffectSystem` (Combat.cs:861) | `Effects` | player's in player.msgpack; creatures' transient | yes |
| `DeathSystem` (Combat.cs:953) | none — dispatches to owners | — | yes |
| `GatheringSystem` (Crafting.cs:58) | `Nodes` | cells.msgpack `harvested_nodes` | no |
| `CraftingSystem` (Crafting.cs:138) | none | — | no |
| `NpcSystem` (Social.cs:105) | `Npcs` (transient bodies) | not saved (companion bodies saved with companions) | yes |
| `RelationshipSystem` (Social.cs:177) | `Relationships` | player.msgpack (schema 10) | no |
| `DialogueSystem` (Social.cs:206) | `Conversations` (heard lines saved; open conversation transient) | player.msgpack | yes |
| `TradeSystem` (Social.cs:468) | none | — | no |
| `QuestSystem` (Quests.cs:70) | `Quests` | player.msgpack (schema 11) | yes |
| `CompanionSystem` (Companions.cs:93) | `Companions` | player.msgpack (schema 12) | yes |
| `QuestDebugger` (QuestDebugger.cs) | none (read-only diagnosis) | — | no |

Tests: `EveryStateSlice_HasExactlyOneOwningSystem` (`tests/Application.Tests/SessionTests.cs:150-160`) and the boot-time checks.

### 3.3 Reads vs writes

- **Any system may read any slice** directly via `_context.State.<Slice>` (FACT: e.g. `CraftingSystem` reads `State.Inventory`,
  `State.Equipment`, `State.Progression`, `State.PlayerCombat`, `Crafting.cs:158-191`). Only writes are gated.
- Read-only "facts" interfaces are used when a pure Domain rule needs world data: `IQuestFacts` (`src/Domain/Quests/Quests.cs:280-295`:
  `Heard`, `Discovered`, `DistanceMm`, `Carried`, `WorldFlag`, `Relationship`), implemented by `QuestSystem`; `IDialogueFacts`
  implemented by `DialogueSystem` (`Social.cs:206`).
- Systems may also keep **transient private fields** outside `RuntimeState`, e.g. `MovementSystem.Intent` (`Systems.cs:123`),
  `CreatureSystem._pending` noises (`Creatures.cs:137`), `QuestSystem._traces` (`Quests.cs:77`). These are not saved, not in the
  digest, and lost on load (INFERENCE: acceptable only for state whose loss after a load is by design, e.g. intent resets to idle).
- The `EntityRegistry` is reached through `State.World.Registry` (internal, `src/World/WorldDelta.cs:237`) and written directly by
  systems (`InventorySystem.NewItem`/`Retire`, `Items.cs:657-664`; `NpcSystem.Populate`, `Social.cs:133-135`) — **not gated by a
  slice** (FACT). WorldDelta mutation, by contrast, is only ever called through gated `RuntimeState` wrappers (FACT: the only
  `World.<Mutator>` calls in `src/World/Runtime` are in `RuntimeState.cs:199-277`).

### 3.4 Cross-system communication

- **Commands down, never reach in.** A system needing another slice changed calls
  `_context.Dispatch(new SomeInternalCommand(...))`, which `Simulation.Dispatch` (`Simulation.cs:369-404`) routes by type to the
  owner's `Handle`. Unknown internal command → `throw InvalidOperationException("No system handles ...")` (`:403`) — a programming
  error, unlike GameCommands.
- **Synchronous and re-entrant**: the owner mutates and publishes before `Dispatch` returns; the owner may dispatch further
  (e.g. `GatheringSystem.Handle` → `ExchangeItems` → inventory; → `PracticeSkill` → progression; → `RecordDeed` → quests;
  `Crafting.cs:100-110`).
- **No transactions.** Atomicity is by discipline: check everything, dispatch the refusable step first, then commit. Example:
  `GatheringSystem` dispatches `ExchangeItems` first and returns its refusal before recording the harvest (`Crafting.cs:100-102`);
  `InventorySystem` "every check happens before anything changes: a refused move leaves every stack as it was" (`Items.cs:109-113`).
  Several callers ignore the result of follow-up dispatches (e.g. `PracticeSkill`, `AwardExperience`).
- The 33 internal commands today: `SetWorldFlag`, `AwardExperience` (Systems.cs); `ChangePools`, `PracticeSkill`, `RecordDeath`,
  `Relocate`, `ApplyEffect`, `ClearEffects`, `Harm`, `Heal`, `EndFight`, `ConsumeItem` (Combat.cs:258-290); `LearnTechnique`,
  `RemoveEffect` (Magic.cs:50-53); `WoundCreature`, `HarmCreature`, `HealCreature`, `CreatureStrike`, `ForgetPlayer`,
  `CorpseEmptied`, `DiscardContainer` (Creatures.cs:96-119); `ExchangeItems`, `Trade`, `AddCurrency`, `GrantItem` (Items.cs:91-106);
  `StartQuest`, `RecordDeed` (Quests.cs:59-62); `ChangeRelationship` (Social.cs:94); `Recruit`, `OrderCompanion`,
  `CompanionStruck`, `PlaceNpc` (Companions.cs:72-81).
- **"Deeds" are the existing observe-an-act pattern**: a system that does something a counting objective may care about dispatches
  `RecordDeed(new Deed(DeedKind, Subject, Count, Quality, NpcId, Tick))` to `QuestSystem` (`src/Domain/Quests/Quests.cs:297-300`;
  `DeedKind { Crafted, Harvested, Killed, Delivered }`). Kill: `Creatures.cs:704-706`; delivery: `Social.cs:355`. INFERENCE: this
  is the nearest existing seam for "the same act affects two factions differently" — an act reported by the acting system to an
  owner via an internal command, not via events.

### 3.5 The M1 scaffold (do not build on it without a decision)

FACT: `ISystem` has `internal void Configure(ICommandBus bus, IEventBus events, IWorldState world, IWorldStateWriter writer)` and
`void Tick(SimulationContext ctx)` (`src/Domain/ISystem.cs:17-39`); `IWorldStateWriter` is `internal interface` with
`Write<T>(EntityId, T) where T : struct` and `Delete<T>` (`src/Domain/IWorldStateWriter.cs:13-31`); `IWorldState` has
`Read/TryRead/Exists<T>` (`src/Domain/IWorldState.cs:12-39`); `ICommandBus.Dispatch<T>(T command, SimulationContext) where T : struct`
(`src/Domain/ICommandBus.cs:11-22`); `SimulationContext(int Tick, int DeltaTicks)` (`src/Domain/SimulationContext.cs:13`, note
`int`, while the runtime uses `long`). `Application.CommandBus` dispatches by reflection on a public `Handle(T, SimulationContext)`
(`src/Application/CommandBus.cs:64-81`); `TickScheduler.RunTick` does nothing (`src/Application/TickScheduler.cs:41-53`). Only
`tests/Domain.Tests/PickUpItemDemo.cs` and `PickUpItemTests.cs` use them. The architecture tests still assert this scaffold's
boundary (`ArchitectureTests.cs:32-41`).

---

## 4. The world delta model (`src/World/WorldDelta.cs`)

### 4.1 One-line model

"Load = generate(baseline) + apply(delta); save = diff against the regenerated baseline (PERSISTENCE.md §1.2). Nothing that equals
the baseline is stored. Reading is public; mutation is internal" (`WorldDelta.cs:208-214`). `docs/PERSISTENCE.md:43`: "Load =
`generate(seed, generator contract) + prove(delta) + apply(delta) + apply(player_state)`. Save = `diff(store, baseline(seed,
generator contract)) + player_state + manifest`."

### 4.2 The baseline and `baseline_hash`

- `CellBaseline(CellKey Cell, string TerrainHash, ImmutableArray<BaselineNode> Nodes, ImmutableArray<BaselinePopulation> Populations)`
  — "a pure function of (generator, world seed, cell key)" (`src/World/Generation.cs:132-171`). Its `Digest` is the cell's
  **`baseline_hash`**: SHA-256 (`CanonicalHasher`, tag `unnamed.cell-baseline/v1`) over cell key, terrain hash, every node
  (key, def, x, z) and every population and slot (`Generation.cs:144-162`). "It covers outputs only, so equal output means
  compatible whatever produced it."
- Generator interface `ICellBaselineGenerator { GeneratorId; WorldgenVersion; RngContractVersion; Profile; Fingerprint;
  Generate(ulong worldSeed, CellKey cell) }` (`Generation.cs:178-197`); implementation `CellBaselineGenerator` (worldgen 2,
  id `unnamed.worldgen.cell-baseline`, `Generation.cs:200-275`). `worldgen_fingerprint` = hash of id, version, RNG contract,
  profile digest, and output on 4 canonical probe cells with seed `0x0DDB1A5E5BAD5EED` (`Generation.cs:281-319`).
- **What the game's baseline actually contains (FACT):** `GameSession.Boot` builds the generator with **empty** `NodeRule` and
  `PopulationRule` lists, the region's terrain rule, and the region's authored nodes as `FixedNode`s
  (`src/Application/GameSession.cs:102-107`). So today a cell's baseline = terrain signature + authored node positions. **Doors,
  structures (static blockers), containers, stations, NPC sites, spawners, switches and barriers are authored region layout
  (`RegionLayout`, `src/Domain/Spatial/RegionLayout.cs:60-98`), NOT part of any `baseline_hash`.**
  INFERENCE: an edit to the authored layout (e.g. moving a wall) does not change any `baseline_hash`, so records placed relative to
  layout (a future player structure next to an authored wall) are not "proven" against layout changes by the existing mechanism.
- Baselines are cached transiently per cell (`WorldDelta.Baseline`, `WorldDelta.cs:240-245`).
- Registered transitions: `BaselineTransition(Name, FromFingerprint, ToFingerprint, DropVanishedTargets)`
  (`src/Persistence/BaselineTransitions.cs:22`); the game registers "M3f: the region's resource nodes" and "M6: the content bible's
  four cells" (`GameSession.cs:108-117`). `SemanticRebase.Apply` carries records onto a new baseline only where the target keeps a
  stable semantic identity (`BaselineTransitions.cs:24-60`).

### 4.3 Record kinds that exist today

| Record (public, in `WorldDelta.cs`) | Keyed by | Fields | Written by (runtime) | Save section |
|---|---|---|---|---|
| Cell world flags (inside `MutableCell.Flags`, `SortedDictionary<string,long>`) | (cell, `world.*` flag id) | value `long`; 0 = baseline = absent | `WorldFlagSystem` via `RuntimeState.SetFlag` | cells.msgpack `flags` (`FlagDto{name,value}`) |
| `NodeHarvest(string NodeKey, long LastHarvestTick, int HarvestSeq)` (`:11`) | (cell, node key `node.<cell>.<name>.<NN>`) | last harvest tick, harvest count | `GatheringSystem` via `RuntimeState.HarvestNode` | cells.msgpack `harvested_nodes` |
| Population alive counts (`MutableCell.PopulationAlive`) | (cell, population id) | int | **nobody at runtime** (tests/M2.Probe only) | cells.msgpack `population_alive` |
| `EntityDeltaRecord(EntityId InstanceId, string SlotKey, int GenerationSeq, string DefId, bool? Alive, int? XCm, int? ZCm, string? BaselineHash)` (`:32-46`) | slot key `<cell>.<population_id>.<NN>` | nullable diverged fields; `DirtyMask` 1=alive, 2=position | **nobody at runtime** (populations are empty) | entities.msgpack `records` |
| `CreatedEntityRecord(EntityId InstanceId, string DefId, string HostCell, int XCm, int ZCm, string? BaselineHash) { Count; Quality }` (`:54-61`) | instance ULID | cm within host cell | `InventorySystem` (drops) via `RuntimeState.PlaceItem`/`TakeItem` | entities.msgpack `created` |
| `ContainerRecord(string Key, EntityId InstanceId, string HostCell, ImmutableArray<ContainerItem> Items, string? BaselineHash)` (`:76`); `ContainerItem(EntityId ItemId, string DefId, int Count) { Quality }` (`:64-68`) | container key (a definition-ID-shaped string, e.g. `container.*`, `corpse.*`, a merchant id) | whole contents with identities | `InventorySystem` via `RuntimeState.SetContainer`/`RemoveContainer` | entities.msgpack `containers` |
| `CreatureRecord(Key, DefId, InstanceId, HostCell, Generation, Condition, XMm, ZMm, FacingMdeg, Health, DiedTick, RespawnTick, BaselineHash) { Mind, Awareness, Knows, KnownXMm, KnownZMm, LastSeenTick, SearchUntil, HasCalled }` (`:162-185`) | `spawner#member` | world mm | `CreatureSystem` via `RuntimeState.SetCreatureRecord`/`RemoveCreatureRecord` | entities.msgpack `creatures` |

`DeltaSnapshot(Cells, Entities) { Created, Containers, Creatures }` is the whole persisted delta (`:188-200`).

### 4.4 What "an entity the player put into the world" looks like today

- **Dropped item stack** (FACT, `Items.cs:584-593`): `MoveItemCommand` to `ItemPlace.Ground` → `InventorySystem.Put` computes the
  cell of the player's body, converts body mm to cm-within-cell (`(body.XMm - minX) / 10`), and calls
  `State.PlaceItem(_owner, cell, id, defId, stack, xCm, zCm, quality)` once per stack (a drop larger than a stack lies as several).
  The item keeps its existing `itm_` ULID (identity travels). `WorldDelta.PlaceItem` validates `0 <= cm < 10000` and `count > 0`
  (`WorldDelta.cs:360-367`). Picked up: `TakeCreated` removes the record; identity lives on in the pack.
- **Changed authored container** (FACT, `Items.cs:513-523`): on first change, `Materialize` creates a `cnt_` instance via
  `registry.CreateEntity(DefinitionId.Parse(site.Key), EntityKind.Container)` and a fresh `itm_` ULID per stack from the loot
  table's deterministic roll (`RngChannel.Open(seed, cell, "loot", site.Key)`, `Items.cs:494-510`); from then on the whole contents
  are recorded. "A changed container stays recorded even if its contents come back to what they were" (`WorldDelta.cs:510-511`).
- **Corpse** (FACT): a creature record with `Condition = Corpse` plus, once touched, an ordinary changed container keyed
  `corpse.<spawner>.m<member>_g<generation>` (`Creatures.cs:153-158`). Looted empty → container removed and `CorpseEmptied`
  dispatched → creature `Condition = Gone` (`Items.cs:557-562`, `Creatures.cs:780-782`).
- `WorldDelta.PlaceCreated(hostCell, defId, xCm, zCm)` (a brand-new created instance with a registry-minted ID, `:347-354`) and
  `RemoveCreated` exist but are **unused by the runtime** (FACT: only tests and `tests/M2.Probe`).
- **There is no rotation, no Y, no owner, no health, no socket on any created record.** The only world-placed "thing with a
  position" persisted is a stack at (xCm, zCm) and a creature at (XMm, ZMm, FacingMdeg).

### 4.5 Save-time diff, rebase and load-time proof

- `TakeSnapshot()` (`WorldDelta.cs:453-521`) is the **authority**: it normalizes every slot record and deletes any that returned to
  baseline (and destroys its registry identity), deletes baseline-equal cells (`MutableCell.IsBaseline`: no flags, nodes or
  population overrides, `:828`), computes `dirty_reasons` from content (`flags`, `nodes`, `spawns`, `entities`; sorted ordinal),
  and stamps every record with its host cell's current `Baseline(...).Digest`. All outputs are sorted (cells by `CellKey`, slots by
  ordinal key, created by instance id, containers/creatures by key via `SortedDictionary`). **Note: it mutates** (rebase side
  effects) although it is a public "read" (allowed by `ArchitectureTests.cs:66-68`).
- `FromSnapshot(generator, seed, registry, snapshot, out rejected)` (`:531-576`) regenerates baselines and applies cells, then
  entities, created, containers, creatures — each `TryApply*` validates and returns a reason; failures become
  `RejectedRecord(Section, Key, Reason)` and are dropped + reported, never trusted. Checks include: `baseline_hash` equals the
  regenerated baseline (`:681-682, 725-726, 748-749, 771-772, 800-801`), IDs not seen twice, not already registered, positions in
  cell, kinds correct (`cnt_` for containers, `crt_` for creatures), enum/range checks. Each applied record registers its IDs.
- Persistence's load sequence (`src/Persistence/SaveLoader.cs:45-190`): manifest → integrity root (quarantine `cells`/`entities`;
  player/manifest corruption fatal, `:73-90`) → schema migration chain (`:99-127`) → decode → definition-ID alias pass over every
  stored def id (`ResolveDefinitions`, `:206-360`: inventory, flags, slot entities, created, container items, progression,
  discoveries, effects, creatures, relationships, conversations, quests, companions) → generator contract check (`:163-172`) →
  baseline proof / registered transitions (`:174-178`) → `WorldDelta.FromSnapshot` (`:180-183`).
- Save files at schema 13 (FACT, `src/Persistence/SaveModel.cs:14-32`): `manifest.json`, `player.msgpack`, `cells.msgpack`,
  `entities.msgpack`, integrity root `sections.sha256`. `SaveFormat.Current = 1` (container version; mismatch refuses),
  `SchemaVersion = 13`, `OldestSupportedSchema = 1`. **No `buildings.msgpack`, `companions.msgpack`, `journal.jsonl`,
  `command_log.jsonl`, `orphans.msgpack`** (companions live in player.msgpack) — `docs/M2_STATUS.md:83` ("arrive with the systems
  that own them").
- Entities section DTO (`src/Persistence/SectionCodec.cs:203-218`): `records`, `created`, `baselines` (cell→hash table; every record
  in a host cell must carry the same single hash, enforced at encode `:478-494`), `containers` (required from schema 6),
  `creatures` (required from schema 8).

### 4.6 The effective-cell digest

`EffectiveCellDigest(CellKey)` (`WorldDelta.cs:582-635`) hashes baseline digest + sorted flags + each baseline node's harvest record
+ created instances in the cell (id, def, x, z, count, quality) + containers in the cell + creatures hosted in the cell (every
field) + population counts and slot occupants. **A new world-delta record type must be added here, or `StateDigest` (and every
digest-based test, smoke and determinism check) will not see it** (INFERENCE from the digest's explicit field list).

---

## 5. Coordinates and numeric types

- Constants (FACT, `src/World/Coordinates.cs:13-24`): `RegionSizeMeters = 2000`, `CellSizeMeters = 100`, `CellsPerRegionAxis = 20`,
  `CellSizeCm = 10000`. `FloorMod(long a, long n) = ((a % n) + n) % n` ("the single shared helper WORLD_ARCHITECTURE.md §3.3
  requires"); `FloorDiv(double value, double divisor) = (long)Math.Floor(value / divisor)`.
- `RegionKey(int Rx, int Rz)` spelled `r_<rx>_<rz>`, negatives as `neg<abs>` (e.g. `r_neg1_neg1`); canonical-only parse
  (`Coordinates.cs:27-69`). `CellKey(RegionKey Region, int Cx, int Cz)` with `Cx, Cz in [0,19]`, spelled `<region>:c_<cx:00>_<cz:00>`
  e.g. `r_0_0:c_07_11` (`:75-130`). Ordering is **ordinal on the string** (`CompareTo`, `:100`). `CellKey.OfWorld(double xMeters,
  double zMeters)` (`:93-96`); callers pass `xMm / 1000.0` (e.g. `Simulation.cs:170-183`, `Items.cs:672`).
- World axes: the ground plane is XZ; Y is height (`WORLD_ARCHITECTURE.md:44`: "Regions tile the XZ plane; Y is entirely
  intra-region height ... No region stacking"). Facing 0 = +Z, increasing towards +X (`Kinematics.cs:30-34`;
  `content/regions/ashen_hollow.yaml:11`).
- Numeric types by use (FACT):
  - Bodies: `sealed record Body(long XMm, long YMm, long ZMm, int FacingMdeg)` (`Kinematics.cs:34`). Y is derived from terrain
    (`TerrainGrid.HeightAtMm`, integer-only, `src/Domain/Spatial/TerrainGrid.cs:50-67`) + jump lift.
  - Intents: `MoveIntent(int DirXPermille, int DirZPermille, Gait, int FacingMdeg)` (`Kinematics.cs:41-59`).
  - Blockers: `long` mm geometry (`src/Domain/Spatial/Blockers.cs:11-125`); collision math in `double`.
  - Delta: created/slot positions `int` cm within the cell; creature records `long` mm world; flags `long`; ticks `long`.
  - Content YAML uses metres (`box_m`, `position_m`) converted to mm at load (`src/Content/WorldContent.cs:146-148, 350-370`).
- **Movement is planar**: "no climbing, nothing stood on" (`Blockers.cs:7-9`); a jump passes over a structure lower than the feet,
  a crouch passes under an overhang (`Blocker.HeightMm`, `ClearanceMm`; `Kinematics.Blocks`, `Kinematics.cs:166-167`). M3 decision:
  "Movement is planar; height comes from the terrain; jump is cosmetic" (`docs/M3_STATUS.md:46`). INFERENCE: this matches owner
  ruling 2 (one storey) — there is no vertical-navigation concept anywhere in the runtime.
- **Crossing cells**: positions are world-mm everywhere in the live runtime; cells matter only for (a) which cell's delta a flag,
  drop or record lives in, (b) tiers, (c) RNG channel keys. The walkable space is **one region-wide `WalkSpace`** (bounds, terrain
  grid, one list of static blockers, `Kinematics.cs:104`), so bodies cross cell seams with no special handling (FACT). Record host
  cells: a drop's host cell is where the body stood (`Items.cs:584-587`); a creature record's host cell is its **spawner's** cell,
  fixed, while its XMm/ZMm roam (`Creatures.cs:802`); a door/switch/barrier flag lives in the cell of its footprint centre
  (`Simulation.cs:169-183`).
- The playable region (Ashen Hollow) is 200 m x 200 m, cells `r_0_0:c_00_00, c_00_01, c_01_00, c_01_01`
  (`content/regions/ashen_hollow.yaml:7-10`). The simulation iterates only the layout's cells (`Simulation.cs:104`); tier radii
  150/600/2000 m + 10 m hysteresis (`content/config/simulation_tiers.yaml`), so "in the hollow every cell is always tier A"
  (`docs/M3_STATUS.md:50`).

---

## 6. Determinism doctrine as implemented

### 6.1 RNG

- `RngChannel.Open(ulong worldSeed, CellKey cell, string subsystem, string semanticKey)` (`src/World/StableRandom.cs:46-56`): channel
  seed = first 8 bytes of SHA-256 over length-prefixed (`"unnamed.rng/2"`, seed, cell key, subsystem, semantic key). Samples:
  `UInt64(uint sample)`, `Int(uint sample, int min, int maxExclusive)` (rejection sampling within the sample's own counter space,
  `:62-76`); SplitMix64 at counter `(sample << 32) | attempt` (`:78-85`). "Integer-only, with no floating point" (`:33-37`).
  `RngContract.V2 = 2` (`:23`). File is named `StableRandom.cs` but holds `RngContract` and `RngChannel` only.
- Channels in use (FACT, grep): generation `"terrain"/"height"`, `"resources"/"<rule>/count"`, `"resources"/"<rule>/<NN>"`,
  `"wildlife"/"<rule>/<NN>"` (`Generation.cs:229-267`); runtime `"combat"/"{attacker}>{target}@{tick}"` where attacker/target are
  **stable names** (`"player"`, creature key, NPC def id — `Combat.cs:582, 655, 834-838`; `Companions.cs:223, 486, 620-623`),
  `"gather"/"{nodeKey}@{harvestSeq+1}"` (`Crafting.cs:98`), `"craft"/"{recipeId}@{tick}"` in the body's cell (`Crafting.cs:192`),
  `"spawn"/key` (`Creatures.cs:183`), `"wander"/"{creatureKey}@{leg}"` (`Creatures.cs:453`), `"loot"/containerKey` (`Items.cs:497`).
- Doctrine: `PERSISTENCE.md:39` (I-9): "Randomness is derived, not sequenced ... There is no global RNG counter in the save, and
  content identity is never an input to a random draw." AGENTS.md: "never from call order or `System.Random`, and never keyed on
  `content_hash`."
- Draws that feed a `double` rule: `channel.UInt64(n) / 18446744073709551616.0` (`Crafting.cs:140, 194`; `Items.cs:502`;
  `Combat.cs:837`).

### 6.2 Forbidden / absent APIs

- No `System.Random`, `new Random(`, `DateTime.Now/UtcNow`, `Stopwatch`, `Environment.TickCount`, `Guid.NewGuid` in `src/Domain`,
  `src/World`, `src/Application` (FACT, grep; `src/World/Legacy` excluded, frozen). `EntityId.NewId` uses
  `DateTimeOffset.UtcNow` + `RandomNumberGenerator` by design (`src/Domain/EntityId.cs:48-53`) — identity, not simulation.
  `GameSession.NewGame` picks a random world seed if 0 via `RandomNumberGenerator` (`GameSession.cs:137-138`).
- Collections: runtime state uses `ImmutableSortedDictionary`/`ImmutableSortedSet` with `StringComparer.Ordinal`
  (`RuntimeState.cs:104-140`); hash sets/dictionaries appear only for membership (`ActionState.Struck` `ImmutableHashSet<EntityId>`,
  `Combat.cs:160`; `WorldDelta._created` `Dictionary<EntityId,...>` always sorted before output, `WorldDelta.cs:437-441, 505-508`).
  `EntityId.GetHashCode` is `StringComparer.Ordinal.GetHashCode(Value)` (`EntityId.cs:127`) — randomized per process in .NET, so
  any iteration over a hash container keyed by `EntityId` would be nondeterministic (INFERENCE: none found in output paths).
- No static mutable state in Domain, EntityRegistry, World, Persistence (architecture test, §8).
- `SYSTEMS.md:37` also requires ordinal comparison and "the numeric policy must be pinned (`double` for simulation state unless a
  schema says otherwise); both are set once in `Directory.Build.props` and asserted in CI" — **there is no `Directory.Build.props`
  in the repo** (FACT: `find` returns none) and persisted simulation state is integer, not double.

### 6.3 Floating point

- `Kinematics.Step` claims "Floating point only in sums, products, quotients and square roots, which IEEE 754 rounds exactly, then
  quantised to whole millimetres: the result is the same on every machine" (`src/Domain/Spatial/Kinematics.cs:116-117, 157-159`).
- But **transcendental math is used in authoritative paths** (FACT): `Math.Sin/Math.Cos` in `Creatures.cs:190, 455, 616-617`,
  `Companions.cs:403`, `src/Domain/Creatures/Perception.cs:96-97`, `src/Domain/Combat/Combat.cs:233-234`; `Math.Pow` in reach checks
  (`Crafting.cs:90, 169`; `Systems.cs:70`). INFERENCE: `Math.Sin/Cos` are not correctly-rounded by IEEE 754 and may differ across
  CPU/runtime; determinism is proven same-machine (tests, playthrough) but not cross-platform. A new navigation/placement system
  should use integer or +−×÷√ math only if it wants the Kinematics guarantee.

### 6.4 Identity and determinism

- Fresh ULIDs (`EntityId.NewId`) for the player character (`GameSession.cs:139`), items (`Items.cs:657`), containers
  (`Items.cs:517`), starting kit (`Simulation.cs:162`). **Deterministically derived IDs** for NPCs
  (`NpcSystem.InstanceIdOf`: `EntityId.Create(EntityKind.Npc, 1, sha256("unnamed.npc/v1", npcId)[0..10])`, `Social.cs:121-125`)
  and creatures (`Identity(key, generation)`, `EntityKind.Creature`, `"unnamed.creature/v2"`, `Creatures.cs:161-166`).
- Consequence (FACT, `docs/M6_STATUS.md:171`): the playthrough rerun matched "79 of 80 rows identical, the other the raw state
  digest, which covers fresh instance IDs and differs every run". `StateDigest` includes instance IDs (player record digest,
  `PlayerState.cs:328-330`; created/containers/creatures in `EffectiveCellDigest`). So `StateDigest` is equal across a
  save/load or a replay **from a save**, but not across two fresh runs of the same script if any ULID was minted.

---

## 7. StateDump, save/reload comparison, replay mechanics

### 7.1 `Simulation.StateDigest()`

`Simulation.cs:357-364`: `CanonicalHasher` over `"unnamed.simulation/v1"`, `WorldTick`, `CaptureRecord().Digest` (the full player
record digest `unnamed.player/v9`, `src/World/PlayerState.cs:322-368`), the cell count, then for each layout cell (sorted) its key
and `World.EffectiveCellDigest(cell)`. "Two simulations are in the same persistent state exactly when these match."
Transient state (intent, combat phase, NPC facing, creature working set beyond its record, tiers, traces) is not in it.

### 7.2 `StateDump` (Application)

`src/Application/StateDump.cs`:
- `Render(Simulation, bool replayable = false)` (`:25-46`): JSON of `{ "world_tick", "player": CaptureRecord(), "world":
  World.TakeSnapshot() }` via `System.Text.Json` with string enums — "Nothing is left out by hand; the records are written as they
  are" (`:13-17`). Any public property on `PlayerRecord` or the `DeltaSnapshot` records is automatically dumped.
- `replayable: true`: removes `AppearanceSeed` and `Digest` from the player, sorts every array of objects by its ID-masked JSON,
  and renames each instance ID to `<kind>#<order of first appearance>` (e.g. `chr#1`, `itm#3`) (`:36-45, 93-190`).
- `Compare(expected, actual, out int leaves)` (`:49-90`): walks both JSON trees; objects by the ordinal union of keys, arrays by index
  (length mismatch reported), leaves by JSON text; returns `"path: expected X, actual Y"` lines; `leaves` counts compared leaves.
- Tests: `ASaveAndALoad_CompareEqual_FieldByField` (save → reload → 0 differences, `leaves > 100`),
  `TheSameScript_PlaysTheSameGame_WhateverTheFreshIdsAre` (two fresh runs equal in replayable mode)
  (`tests/Application.Tests/StateDumpTests.cs:18-60`).
- The acceptance playthrough writes `state_saved.json` and `state_replay.json` before quitting, and on relaunch
  `state_loaded.json` + `state_diff.txt` (`src/Presentation/Playthrough.cs:421-460`). M6/closeout: "415 fields compared,
  0 differences" (`docs/M6_STATUS.md:172, 214, 218`), 2,499 commands identical with IDs masked.

### 7.3 Replay mechanics

- The only replay artifact is the in-memory `Simulation.CommandLog` (`IReadOnlyList<LoggedCommand>`, `Simulation.cs:260`) and the
  playthrough's `commands.tsv` (`Playthrough.cs:524-530`: `tick \t sequence \t command.ToString() [\t rejected: reason]`). The
  `.tsv` is human-readable record `ToString()`, not a re-parseable format (INFERENCE).
- `AScripted200CommandSession_ThroughFrames_EqualsItsReplayThroughTheCommandBus`
  (`tests/Application.Tests/DeterminismAndViewTests.cs:48-77`): play 200 random `MoveCommand`/`InteractCommand`s through uneven
  frames from a loaded save, then on a second load of the same save: `while (WorldTick < entry.Tick) Step(); Enqueue(cmd);
  DrainCommands();` for every logged entry, step to the same final tick, assert equal `StateDigest()` and equal
  `(Tick, RejectedReason)` sequences. This is PROTOTYPE C3 (`docs/PROTOTYPE.md:279`).
- `AViewThatListensToEverything_SeesExactlyWhatHappened_AndWritesNothing` (`:79-126`): a session with subscribers (one tries to
  mutate an event copy) ends with the same digest as an unobserved control; rejection/door/flag/movement/discovery/XP events match
  the log exactly.
- `PERSISTENCE.md` T-28 (saved log replay, `docs/PERSISTENCE.md:301`) is **not implemented**: `CommandLogSha256 = null`
  (`src/Persistence/SaveStore.cs:474`; asserted `tests/Persistence.Tests/RoundTripTests.cs:57`).

---

## 8. Architecture tests — what is enforced and how (verbatim names)

`tests/Architecture.Tests/ArchitectureTests.cs` (14 tests counting theory cases):

1. `OnlyPresentation_MayReferenceGodot(string assembly)` (`:25-30`) — Domain, Application, Content, EntityRegistry, World,
   Persistence reference no assembly whose name starts with "Godot".
2. `TheWorldStateWriter_IsNotVisibleOutsideDomain` (`:32-41`) — `UNNAMED.Domain.IWorldStateWriter` is not visible;
   `ISystem.Configure` `IsAssembly` ("ISystem.Configure must be internal"). (Scaffold only.)
3. `ApplicationAndPersistence_CannotWriteAuthoritativeState` (`:43-55`) — Domain and World grant `InternalsVisibleTo` to neither
   Application nor Persistence; every friend ends in `.Tests` or is `M2.Probe`.
4. `WorldDelta_ExposesNoPublicMutation` (`:61-79`) — the public method set of `WorldDelta` must **equal** exactly: `Baseline`,
   `GetFlag`, `IsHarvested`, `GetPopulationAlive`, `Occupant`, `TakeSnapshot`, `EffectiveCellDigest`, `FromSnapshot`, `CreatedIn`,
   `FindCreated`, `Container`, `ContainersIn`, `Creature`, `CreaturesIn`, `NodeRecord`, `get_Generator`, `get_WorldSeed`. "A new
   public member must be added here deliberately."
5. `StateAssemblies_HoldNoStaticMutableState` (`:85-105`) — in Domain, EntityRegistry, World, Persistence (not Application, not
   Content): every static field must be `const`/compiler-generated or `readonly` and not a mutable container (`T[]`, `List<>`,
   `Dictionary<,>`, `HashSet<>`, `SortedDictionary<,>`, `Queue<>`, `Stack<>`, `StringBuilder`; `:204-209`). MessagePack-generated
   types exempt.
6. `TheSimulation_ExposesOnlyReadsAndTheCommandPath` (`:111-131`) — `Simulation`'s public non-special methods must equal exactly:
   `Start`, `NewCharacter`, `CellOf`, `Enqueue`, `DrainCommands`, `Step`, `CaptureRecord`, `StateDigest`, `Wares`, `Diagnose`,
   `Aim`; and **no public property may have a setter**. (Public get-only properties can be added freely.)
7. `ViewsAndEvents_HaveNoPublicSetters` (`:134-155`) — every public record/struct in `UNNAMED.World.Runtime` and
   `UNNAMED.Domain.Spatial`: no public non-init setter.
8. `PresentationSource_NeverReachesPastThePublicReadAndCommandSurface` (`:162-183`) — no line in `src/Presentation/**/*.cs`
   (excluding `.godot/`) contains: `BindingFlags.NonPublic`, `UnsafeAccessor`, `InternalsVisibleTo`, `.SetValue(`,
   `Activator.CreateInstance`, `System.Runtime.CompilerServices.Unsafe`, `.Step()`, `.DrainCommands()`, `IWorldStateWriter`,
   `SaveStore`.
9. `TheRegistry_OwnsIdentityOnly_AndDependsOnNothingButDomain` (`:195-202`).

Enforced elsewhere: slice ownership at boot (`RuntimeState.Claim/RequireEverySliceOwned/Require`) and
`EveryStateSlice_HasExactlyOneOwningSystem` (`SessionTests.cs:150-160`); `Commands_CarryNoCameraState_SoEveryPerspectiveSubmitsTheSameThing`
(`SessionTests.cs:162-178`: no `GameCommand` property name may contain `camera`, `view`, `perspective`, `zoom`); historical save
fixtures (`tests/Persistence.Tests/Fixtures/README.md`). **Not enforced by any test:** a Domain/World system writing another
system's slice by reaching into `RuntimeState` with a borrowed `SliceOwner` (impossible without the token), cross-slice *reads*
(allowed), registry writes (ungated), and "two worlds in one process" beyond the static-field scan
(`docs/ARCHITECTURE.md:237` asks for it; no runtime two-simulation test found).

---

## 9. Recipe: a new system with a new command, event, state slice and persistent delta

Pattern example to copy: `GatheringSystem` (owns a slice persisted in the world delta) and `RelationshipSystem` (owns a slice
persisted with the player). Steps, in the order the code forces:

**Step 1 — Pure rules in Domain.** `src/Domain/<Feature>/<Feature>.cs`: immutable definitions (records built from content), pure
static functions (`XRules.Evaluate(...)`) over them; no World types, no files, no RNG other than a passed-in `Func<uint,double>` or
values drawn by the caller. Precedent: `CraftingRules.Ready/Roll`, `QuestRules.Advance/Record`, `Relationships.Clamp`
(`src/Domain/Social/Social.cs:32-39`).

**Step 2 — Content.** A closed content kind must exist in `src/Content/SchemaResolution.cs` (`faction` already exists, `:158-164`;
there is **no** building/piece/structure kind — the list is item, creature, npc, spell, ability, effect, recipe, resource, quest,
dialogue, faction, loot, merchant, affix, node, spawn, location, config, skill, species, schedule, set, region, anchor, fact,
world_flag). Add `src/Content/<Feature>Content.cs` with `Validate(ContentLoader)` (hooked in `ContentLoader.LoadAll`, pattern
`src/Content/ContentLoader.cs:170-190`) and `Build(ContentLoader)` returning a `<Feature>Setup`. Tuning goes in
`content/config/<feature>.yaml`.

**Step 3 — Setup.** In `src/World/Runtime/<Feature>.cs`: `public sealed record <Feature>Setup(...) { public static <Feature>Setup Empty ... }`.
Add `public <Feature>Setup <Feature> { get; init; } = <Feature>Setup.Empty;` to `SimulationSetup` (`Simulation.cs:14-33`), and set it
in `GameSession.Boot` (`src/Application/GameSession.cs:88-99`).

**Step 4 — Commands (public).** `public sealed record <Verb>Command(EntityId Actor, ...) : GameCommand(Actor);` — intent only, no
behaviour, no camera/view/zoom fields, stable string keys or `EntityId`s for targets (never engine node paths). Add an arm to the
`DrainCommands` switch (`Simulation.cs:273-299`): `<Verb>Command c => _<feature>.Handle(c, WorldTick),`.

**Step 5 — Internal commands (for other systems to ask this one).** `internal sealed record <Verb> (...) : InternalCommand;` with a
doc comment "To <see cref="XSystem"/>: ...". Add an arm to `Simulation.Dispatch` (`Simulation.cs:369-404`) passing `Now`.

**Step 6 — Events (public).** `public sealed record <PastTense>(..., long Tick);` Publish with `_context.Events.Publish(...)` after
the mutation. Views only. Never subscribe from a system; if another system must react, the emitter dispatches an internal command to
it (the `RecordDeed` pattern, §3.4).

**Step 7 — State slice.** Add `StateSlice.<Feature>` with a doc comment saying saved/transient and where (`RuntimeState.cs:19-74`).
Add to `RuntimeState` a get-only/`private set` property (use `ImmutableSortedDictionary<string, T>` with `StringComparer.Ordinal`)
and one setter method per mutation that starts with `Require(owner, StateSlice.<Feature>)` (pattern `SetRelationship`,
`RuntimeState.cs:296-302`). If the slice is saved with the player, initialize it from `PlayerRecord` in the `RuntimeState`
constructor (`:97-115`).

**Step 8 — The system.** `internal sealed class <Feature>System` with `/// Owns: <see cref="StateSlice.<Feature>"/> - ...` header;
constructor `(SystemContext context, SliceOwner owner, EntityId player, ...)`; `public string? Handle(...)` returning `null` or a
reason (check `command.Actor != _player` first; check everything before mutating; dispatch refusable cross-system steps first);
`public void Tick(long tick)` if it evolves with time; `public ImmutableArray<<Feature>View> Views()`.

**Step 9 — Compose.** In the `Simulation` constructor add a field and
`_<feature> = new <Feature>System(_context, _state.Claim(nameof(<Feature>System), StateSlice.<Feature>), player.Id);` at a deliberate
position (`Simulation.cs:115-137`; `RequireEverySliceOwned` at `:139` fails boot otherwise). If it ticks, insert
`_<feature>.Tick(tick);` at a deliberate place in `Step` (`:320-333`) and update the Step doc comment (`:308-311`), remembering
quests run after everything they read and the clock is last. Expose `public ImmutableArray<<Feature>View> <Feature>s => _<feature>.Views();`
(a get-only property needs no test change; a new public **method** must be added to `ArchitectureTests.cs:114-122`).

**Step 10 — Randomness.** `RngChannel.Open(State.World.WorldSeed, <cell of the act>, "<new subsystem name>", $"<stable key>@<tick or seq>")`.
Keys must be stable names (content ids, `spawner#member`, `"player"`), not fresh ULIDs, or replays/new games diverge (§6.4).

**Step 11 — Persistence. Choose the lightest form that fits:**
- (a) **A per-cell scalar** → a `world.*` flag: declare `content/world_flags/<area>/<name>.yaml` (`kind: world_flag`, `type`,
  `default: 0`, `owning_system`; example `content/world_flags/hollow/forge_shed_door_open.yaml`) and dispatch
  `SetWorldFlag(cell, flagId, value)`. No schema change. (0 is absence; values are `long`.)
- (b) **Player-relative state** (e.g. per-faction standing, like relationships) → a field on `PlayerRecord` (`src/World/PlayerState.cs`),
  validated in its constructor, included in `Digest` (bump the digest tag `unnamed.player/v9`), carried by every `With*` copy method
  (`PlayerState.cs:222-253`), captured in `Simulation.CaptureRecord` (`Simulation.cs:342-351`), a `PlayerDto` field in
  `SectionCodec.cs`, definition ids added to `SaveLoader.ResolveDefinitions`.
- (c) **World-placed records** (e.g. placed pieces) → a new `public sealed record` in `WorldDelta.cs` with `HostCell` and
  `string? BaselineHash`; a private sorted store; `internal` mutators; public readers (and add them to the allowed list in
  `ArchitectureTests.cs:64-72`); inclusion in `TakeSnapshot` (sorted, stamped with `Baseline(host).Digest`, a `dirty_reasons` value),
  a `DeltaSnapshot` init property, `FromSnapshot` + a `TryApply<X>` validator (hash, ids, registry, ranges), `EffectiveCellDigest`;
  `RuntimeState` gated wrappers; DTO + encode/decode in `SectionCodec` (inside `EntitiesSectionDto` or a new section file);
  alias pass; `SemanticRebase` behaviour for the new record under a baseline transition.
- (d) **A new section file** (`buildings.msgpack` per `PERSISTENCE.md` §5.4) → also `SaveFormat` constants and `CheckedFiles`
  (`SaveModel.cs:24-32`), quarantine semantics in `SaveLoader` (`:68-98`; PERSISTENCE says a corrupt `buildings` section loads
  without it and names every lost structure, `docs/PERSISTENCE.md:136, 458`).
- For (b)–(d): **schema bump** (`SaveFormat.SchemaVersion` 13 → 14), the 13→14 step in `SchemaMigrations.Production`, freeze the
  schema-13 section shapes as `Sections/SchemaV13.cs`, add fixture `tests/Persistence.Tests/Fixtures/v14/` written by `M2.Probe`,
  update every `expected.json` whose current state gained a field (`tests/Persistence.Tests/Fixtures/README.md:12-24`; "A fixture
  is ... never edited").

**Step 12 — Tests.** Application.Tests through `GameSession` + `Harness` (`tests/Application.Tests/Harness.cs`) — command accepted/
rejected with reason, events, views; save → load → `StateDump.Compare` = 0 differences; a replay test that re-applies the command log
at the same tick boundaries and compares `StateDigest`; Persistence round-trip and fixture; Content validation tests.

---

## 10. Existing extensibility seams for structures, buildings, factions, navigation

### 10.1 Buildings / structures

- `EntityKind.Building` → prefix `bld`; also `Container` `cnt`, `FarmPlot` `plt` (`src/Domain/EntityKind.cs:12-50`). "Every other
  instance (container, corpse, building, summon, ...) must be created with an explicit kind" (`:66-70`) —
  `registry.CreateEntity(defId, EntityKind.Building)`.
- **Static structures today are authored blockers**: region YAML `structures:` entries `{ id, box_m: [minx,minz,maxx,maxz] | circle_m:
  [x,z,r], height_m, clearance_m? }` (`content/regions/ashen_hollow.yaml:64-70`; parser `src/Content/WorldContent.cs:146-148,
  350-370`) become `BoxBlocker`/`CircleBlocker` in `WalkSpace.Blockers` (`src/Domain/Spatial/Kinematics.cs:104`). Axis-aligned boxes
  only ("Structures in the greybox are axis-aligned, which keeps the math exact", `Blockers.cs:40`). They are immutable content.
- **Dynamic blockers seam**: `SystemContext.Obstacles()` (`Systems.cs:82-86`) = closed doors + standing barriers + living creatures +
  non-companion NPCs; passed to `Kinematics.Step(... dynamicBlockers ...)` by every mover, and published to presentation as
  `Simulation.DynamicBlockers` for client prediction (`Simulation.cs:254-255`; `src/Presentation/Player/PlayerController.cs:127`).
  INFERENCE: placed walls would be added here (or `WalkSpace` rebuilt) so both authority and prediction see them; a rotated wall
  needs a new `Blocker` shape because only axis-aligned boxes and circles exist.
- **Doors**: `DoorSite(Key, FlagId, BoxBlocker ClosedFootprint)` + a `world.*` flag in the footprint's cell; interaction via
  `InteractCommand(Actor, TargetKey)` → `InteractionSystem` → `SetWorldFlag` (`RegionLayout.cs:12`, `Systems.cs:251-273`). Door keys
  must start `door.`; one flag per door (`WorldContent.cs:153-165`). Closing is refused if a body is in the doorway (`Systems.cs:267-268`).
- **Switches/barriers** (M6): flag-driven, authored (`RegionLayout.cs:20-28`).
- **Stations**: `StationSite(Key, Kind, XMm, ZMm)`, authored only; `CraftingSystem` checks `Layout.Stations.Any(kind && within reach)`
  (`Crafting.cs:168-170`). INFERENCE: a placeable station needs a dynamic station list the crafting check also consults.
- **Containers**: authored `ContainerSite(Key, LootTableId, XMm, ZMm, StackSlots)`; runtime corpses and trader wares are synthesized
  sites (`SystemContext.FindContainer/CorpseSites/MerchantSites`, `Systems.cs:51-76`). A placed storage chest would be a new
  synthesized site kind with a `ContainerRecord` (the record type already carries `HostCell` and a `cnt_` id).
- Quest seams: `construct_building` is in `ObjectiveTypes.NotBuilt` ("building (M7)"); rewards `property`, `access`, `reputation`
  not built (`src/Domain/Quests/Quests.cs:141-160, 206-207`).
- Docs seams: `PERSISTENCE.md:248-252` (§5.4 buildings.msgpack: "A building is a ULID-keyed structure with a footprint of one or more
  cells; pieces are ULID-keyed rows with a `def_id` and a socket path"; `storage` references container ULIDs in entities.msgpack);
  `PERSISTENCE.md:210` lists `buildings` as a `dirty_reasons` value (code's `TakeSnapshot` only emits flags/nodes/spawns/entities,
  `WorldDelta.cs:489-494`); `WORLD_ARCHITECTURE.md:403-420` (§10); `SYSTEMS.md:350-357` (S-32: `PlacePiece(pieceDefId, socketId,
  rotation)`, `RemovePiece`, `DamagePiece`, `RepairPiece`, `AssignOccupant`, `IsValidPlacement`; events `PiecePlaced`, `PieceRemoved`,
  `PieceDamaged`, `PieceDestroyed`, `BuildingCompleted`, ...); `DATA_MODEL.md:148` (building piece definition vs instance fields).

### 10.2 Factions / reputation

- Content kind `faction` registered (`src/Content/SchemaResolution.cs:158-164`) with an empty `FactionSchema` placeholder
  ("Faction-specific properties would be added here", `src/Content/SchemaTypeMapper.cs:250-256`); no `content/factions/` directory
  exists (FACT: `ls content`). `faction_ref` reference type is known to content checks (`src/Content/ContentChecks.cs:34`), but a
  quest's `faction_ref` is refused as not built (`src/Content/QuestContent.cs:29`; `DATA_MODEL.md:517`).
- Quest objective types `faction_reputation`, `faction_state` → "factions (M7)" (`src/Domain/Quests/Quests.cs:154-155`).
- **Relationships (personal, per NPC)** exist: 5 dimensions `affection, fear, grudge, respect, trust`, each `int` in [-100, 100], 0 not
  stored (`src/Domain/Social/Social.cs:32-39`; `RuntimeState.cs:292-302`); saved as `RelationshipValue(NpcId, Dimension, Value)` with
  the player (`PlayerState.cs:38`); changed only through `ChangeRelationship(NpcId, Dimension, Delta, EventKey)` →
  `RelationshipChanged(NpcId, Dimension, From, To, EventKey, Tick)` ("every change is attributed", `Social.cs:59, 94, 188-196`).
  Dialogue reads them (`RelationshipCondition`) and writes them (`RelationshipEventConsequence`) (`src/Domain/Social/Social.cs:77, 110`).
  INFERENCE: these are the "personal relationship / trust / fear / grudge" axes of owner ruling 3 — already separate from any faction
  standing, which must be a distinct slice.
- **There is no attack on NPCs**: the player's blows trace only creatures (`CombatSystem.Trace` returns `CreatureState?`,
  `Combat.cs:523`; no `Npcs` reference in `Combat.cs`). There is no crime, witness, law, bounty or legal-status state anywhere
  (FACT by grep). Creatures have "no shared awareness: a packmate out of earshot of a howl knows nothing" (`Creatures.cs:124-125`) —
  an existing precedent for "information is not magically global".
- NPC identity: derived IDs, bodies transient, placed from `NpcSite`s (`Social.cs:118-142`). NPC definitions carry no faction field
  in the runtime (`NpcDefinition`; INFERENCE from absence of any faction reference in `src/World`).

### 10.3 Navigation / pathfinding

- **No path search exists in `src/`** (FACT: grep for navmesh/pathfind/A*/NavigationServer/NavigationAgent/NavigationRegion finds
  nothing outside docs).
- Movers today: player — `Kinematics.Step` with circle-vs-blocker push-out and sliding, sub-stepped at half the body radius, bounds
  clamp (`Kinematics.cs:129-217`). Creatures — straight-line `MoveIntent` towards a goal via `Kinematics.Step` (`Creatures.cs:820-837`),
  role-driven wander/patrol derived from the tick (`Creatures.cs:418-455`). Companions — follow a breadcrumb trail of the player's
  positions (`TrailMark`, mark every `TrailStepMm`, at most `TrailLength`), steer to the farthest mark in clear view, and if too far
  (`CatchUpBeyondMm`) or stuck (`SnagTicks` without headway) **teleport** near the player via `CatchUp`/`Ring`
  (`Companions.cs:318-411`; `src/Domain/Companions/Companions.cs:34-67`). The trail is persisted (schema 12).
- `ITierSimulation` for tiers B/C is a stub (`Systems.cs:514-531`), "B: coarse movement on routes; C: schedule bands and economy".
- INFERENCE: owner ruling 1 (deterministic, headless nav inside the simulation, rebuilt from authoritative structure state, working
  across cell seams) has **no conflicting code**: the runtime's world is already one region-wide planar `WalkSpace` in integer mm with
  deterministic collision, so a grid/graph built from `WalkSpace.Blockers` + dynamic blockers is a natural fit; cell seams do not
  exist in the movement space.

---

## 11. Owner rulings — where recorded in the repo/handoffs (for this topic)

| Ruling | Recorded? | Where | Conflicts |
|---|---|---|---|
| 1. Godot Navigation not authoritative; deterministic headless nav in the sim | **Not found** in e10d2c4 docs or the V4 handoff (grep for NavigationServer/"Godot navigation"/navmesh in untracked docs: only "navigation confusion", `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:594`, untracked, authority unconfirmed) | — | `docs/WORLD_ARCHITECTURE.md:435` puts "Navmesh — Recast-style, baked per cell, stitched at cell borders" in §11 *Streaming, LOD, and the presentation contract*; `:414` "Navmesh around buildings is rebuilt per cell on change"; `:137` "Collision, navmesh, occlusion — Baked"; `docs/PERSISTENCE.md:83` "Navmesh, collision ... — Baking / streaming pipeline"; `docs/ROADMAP.md:284-285` M7 exit "the navmesh updates on placement", proof "navmesh path test". These read as an engine-baked per-cell navmesh; none says Godot is authoritative, but "per cell ... stitched" is the model the ruling moves away from. `RISK_REGISTER.md` RK-14 (`:280-294`) frames the seam problem in per-cell-bake terms. |
| 2. Building v1 is one storey | **Not found** as such | — | `docs/ROADMAP.md:283` lists "foundations, walls, floors, roofs, doors; free rotation and socketing" (INFERENCE: "floors" is ambiguous, "free rotation" vs snap). The runtime is planar (`docs/M3_STATUS.md:46`; `Blockers.cs:7-9`) — consistent with the ruling. |
| 4. C10 retired | Yes | `docs/PROTOTYPE.md:298` ("Revised by owner ruling (2026-09-24) ... No single encounter is required to force every combat tool") | — |
| 5. No core action requires a radial | Yes (untracked) | `G:/UNNAMED/docs/OTHERREACH_MASTER_HANDOFF_2026-09-24_V4.md:78-82` (untracked, authority unconfirmed) | none in sim code (commands are plain records; no radial concept) |
| 6. No networking; preserve seams | Yes | `docs/ARCHITECTURE.md:12, 342`; `docs/SYSTEMS.md:25` (C-10: "Only the command/event seam and stable identities exist as extension points"); V4 handoff `:131, 1514` | none |
| 7. Engine-independent C#, commands in, dotted IDs, ULIDs, sparse deltas | Yes | `docs/ARCHITECTURE.md:12`; `docs/DATA_MODEL.md:123`; V4 handoff `:115-122`; enforced by §8 tests | Hand-built instance IDs for NPCs/creatures (§6.4) vs AGENTS.md "never build one by hand" and `DATA_MODEL.md:123` "Generated **only** by the Entity Registry". |

Ruling 3 (factions) is outside this topic except for the seams in §10.2.

---

## 12. Contradictions found

1. **ARCHITECTURE §4.2 vs code — method name.** Doc: `void Register(ICommandBus bus, IEventBus events, IWorldState world,
   IWorldStateWriter writer);` (`docs/ARCHITECTURE.md:135`). Code: `internal void Configure(...)` (`src/Domain/ISystem.cs:27-31`).
2. **ARCHITECTURE §4.2/§5 vs code — which pattern is live.** Doc describes `ISystem` + `IWorldStateWriter` as how systems work
   (`docs/ARCHITECTURE.md:131-143, 228-231`); the live runtime uses `SliceOwner`/`RuntimeState` in World and never implements
   `ISystem` (`docs/M3_STATUS.md:45`; `src/World/Runtime/RuntimeState.cs:77-161`).
3. **ARCHITECTURE §5 status stale.** "(b) waits for the typed component-key registry, which does not exist yet"
   (`docs/ARCHITECTURE.md:231`), but `StateSlice` + `Claim` + `RequireEverySliceOwned` implement "every component key is claimed by
   exactly one system" at boot (`RuntimeState.cs:14-18, 144-161`) with a test (`SessionTests.cs:150-160`).
4. **ARCHITECTURE §4.3 "Systems may publish and subscribe"** (`docs/ARCHITECTURE.md:157`) vs code: no system subscribes; cross-system
   reaction is by internal command.
5. **ARCHITECTURE §4.5 "mark cell/entity dirty for the save delta"** (`:174-177`) vs code: no dirty set; the save-time diff in
   `TakeSnapshot` is the only mechanism (consistent with `PERSISTENCE.md:219` "the diff is mandatory", but not with the §4.5 flow).
6. **ARCHITECTURE §4.1 command shape** `readonly record struct ... : ICommand` (`:112-119`) and `ICommandBus.Dispatch<T> where T :
   struct` (`src/Domain/ICommandBus.cs:21`) vs live `public abstract record GameCommand(EntityId Actor)` (reference type,
   `src/World/Runtime/Commands.cs:14`).
7. **SYSTEMS §1 execution model vs code (four points).** (a) "Drain the queue in a deterministic system order — the order systems are
   listed in §2 ... ties broken by ascending instance ID" (`docs/SYSTEMS.md:32`) vs FIFO submission order (`Simulation.cs:271`).
   (b) "Events are queued during the drain and delivered synchronously at the end of the tick ... Views therefore never observe a
   half-applied tick" (`:34`) vs immediate inline publication (`EventBus.cs:50-60`). (c) "`CommandRejected` with a machine-readable
   reason code" (`:35`) vs free-text `string Reason` (`Events.cs:13`). (d) "numeric policy ... `double` for simulation state ...
   set once in `Directory.Build.props` and asserted in CI" (`:37`) vs integer-mm state and no `Directory.Build.props`.
8. **Command log persistence.** `PERSISTENCE.md:296` ("The command log is persisted for every save, and replayed by a test") and
   `SYSTEMS.md:37, 50` vs code: never written (`SaveStore.cs:474`; `RoundTripTests.cs:57`), acknowledged in `docs/M3_STATUS.md:52`.
   T-28 is therefore unimplemented.
9. **Save layout.** `PERSISTENCE.md:106-121` (§3.2) lists `companions.msgpack`, `buildings.msgpack`, `journal.jsonl`,
   `command_log.jsonl` vs code's four files (`SaveModel.cs:24-32`); companions actually live in `player.msgpack` (schema 12).
10. **`dirty_reasons` vocabulary.** `PERSISTENCE.md:210` enumerates `nodes, spawns, flags, entities, buildings, terrain`; code emits only
    `flags, nodes, spawns, entities` (`WorldDelta.cs:489-494`), and `MutableCell.IsBaseline` ignores anything but flags/nodes/spawns.
    `WORLD_ARCHITECTURE.md:411` says owning cells are "marked dirty with reason `buildings`".
11. **Identity doctrine vs derived IDs.** `src/Domain/EntityId.cs:17-21` ("Instance IDs are runtime identity only and are never derived
    from a seed"), `DATA_MODEL.md:123` ("Generated only by the Entity Registry"), AGENTS.md ("never build one by hand") vs
    `NpcSystem.InstanceIdOf` and `CreatureSystem.Identity` building IDs with `EntityId.Create(kind, 1, sha256(...))`
    (`Social.cs:121-125`, `Creatures.cs:161-166`). (Derived from content keys, not the world seed; registered with the registry.)
12. **Determinism claim vs transcendental math.** `Kinematics.cs:116-117` / `PERSISTENCE` "same on every machine" spirit vs
    `Math.Sin/Cos` in creature/companion/perception/combat paths (§6.3). Replays are proven same-machine only.
13. **ARCHITECTURE §5 ownership table vs S-27.** "Reputation — Faction system — Derived from standings; not separately stored"
    (`docs/ARCHITECTURE.md:208`) vs S-27 "Persistent. Reputation values and tiers" (`docs/SYSTEMS.md:307`). (Flag for the faction
    reader.)
14. **ARCHITECTURE §5 "Position (actors) — Movement system"** (`:191`) vs code: movement owns only `PlayerBody`; creature bodies are
    `CreatureSystem`'s, NPC bodies `NpcSystem`'s (`Simulation.cs:117, 126, 131`).
15. **ARCHITECTURE §5 two-world test** "Two `WorldState` instances ... The same test suite carries a second invariant" (`:235-237`) — only
    the static-field scan exists; no runtime two-simulation disjointness test found (INFERENCE from grep).
16. **Navmesh model vs owner ruling 1** — see §11 (`WORLD_ARCHITECTURE.md:414, 435`; `PERSISTENCE.md:83`; `ROADMAP.md:284-285`).
17. Minor: `GameSession.cs:75-76` has two consecutive `<summary>` blocks (the `Boot` summary is attached to the `M3LayoutFingerprint`
    constant). `SimulationContext.Tick` is `int` (`src/Domain/SimulationContext.cs:13`) while runtime ticks are `long`.

---

## 13. Design-relevant implications for M7 (INFERENCE, clearly mine)

- **New systems slot in cleanly** if they follow §9; the cost centres are persistence (schema bump + fixture + alias pass + digest +
  dump are all mandatory) and the architecture allow-lists.
- **Placement is a GameCommand** (`PlaceXCommand(Actor, pieceDefId, anchor/socket, rotation)`), validated in the system against
  authoritative `WalkSpace`/dynamic blockers/bodies (never physics), producing a world-delta record with a registry ULID
  (`bld_`) and host cell + `baseline_hash`. Replays that mint ULIDs make raw `StateDigest` differ across fresh runs; use
  `StateDump(replayable: true)` for fresh-run comparisons or replay from a save.
- **Placed geometry must feed both authority and prediction**: `SystemContext.Obstacles()` and `Simulation.DynamicBlockers` (or a
  rebuilt `WalkSpace`), since presentation predicts with the same `Kinematics.Step`. Rotated pieces need either axis-aligned
  snapping (0/90/180/270 on a grid) or a new exact `Blocker` shape.
- **Any nav structure** should be derived (transient, rebuilt on load and on structure edit, never saved — `PERSISTENCE.md:84`
  already classes pathfinding as reconstructed state), keyed off authoritative state, integer-based, and consulted by movers that
  today steer straight (creatures) or breadcrumb + teleport (companions).
- **Faction reactions to acts**: follow the `RecordDeed` pattern — the acting system dispatches an internal "act witnessed/recorded"
  command to the faction owner; do not add event subscriptions to systems (would contradict the live pattern and complicate replay).
  Per-witness knowledge needs perception, which exists for creatures (sight cone, sound radius, calls) but not for NPCs.
- **A cell's `baseline_hash` does not cover authored layout.** If M7 validates placements against authored structures, a layout edit
  will not be caught by baseline proof; a separate layout digest or a transition policy would be needed.

---

## 14. Open questions

1. Is the M1 scaffold (`ISystem`, `IWorldStateWriter`, `CommandBus`, `TickScheduler`) to be retired, or kept as "Domain-internal
   stores" (`docs/M3_STATUS.md:45`)? ARCHITECTURE.md still presents it as the system model.
2. Should M7 persist the command log (`command_log.jsonl`, T-28) given building placement makes replay-from-save the main proof?
3. Which document wins on event delivery timing — code (inline) or SYSTEMS §1 (queued to end of tick)? M7 views of placement
   ghosts/validation may care.
4. Buildings: new `buildings.msgpack` section (per PERSISTENCE §5.4, quarantinable) or records inside `entities.msgpack`
   (existing pattern)? Does a building record's host cell carry `baseline_hash` proof like other records, given pieces are "regenerable
   from nothing"?
5. Where does faction standing persist — with the player (`PlayerRecord`, like relationships) or as world state (faction-to-faction
   relations, war state are not player-relative)? ARCHITECTURE §5 vs S-27 disagree on storing reputation.
6. Are NPCs to become attackable (crime/attack legality) in M7? Combat currently cannot target NPCs.
7. Should the transcendental-math determinism gap (§6.3) be closed before a nav system depends on creature/companion steering?
8. Should derived NPC/creature IDs be ratified as doctrine (they contradict `DATA_MODEL.md:123` / AGENTS.md wording)?
9. Owner rulings 1 and 2 are not written in any repo document or handoff found; where should they be recorded (DECISIONS.md?) and
   should `WORLD_ARCHITECTURE.md` §10/§11 and `ROADMAP.md` M7 exit criteria ("the navmesh updates on placement") be revised?
