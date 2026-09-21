# ARCHITECTURE.md — Runtime Architecture

**Project:** UNNAMED (working title) — first-person, solo-first, open-world fantasy RPG
**Phase:** 0 — Vision and Architecture
**Engine:** Godot 4.x, C# (decision `D-01`)
**Authority:** `PROJECT_CHARTER.md` governs creative intent. `DECISIONS.md` governs technical intent. This document describes *how the pieces fit together* and defers to both.

---

## 1. The one-paragraph architecture

The game is a **single-player application built with a server-shaped core**. All authoritative gameplay state lives in an engine-agnostic C# domain layer that knows nothing about Godot. The Godot layer renders that state and translates player input into **commands**. Commands are validated and applied by **systems**, which own specific slices of state and nothing else. Applied changes emit **events**; presentation subscribes and reacts. Nothing mutates state except by applying a command (`D-02`, `D-11`). This gives us headless testability today and a credible path to LAN/co-op authority later, without writing a single line of networking code now (`D-12`).

The architecture exists to satisfy four non-negotiables from the charter:

1. **The world persists** — so state must serialize cleanly and identities must be stable.
2. **The world does not revolve around the player** — so simulation must be tiered, not player-attached.
3. **Content scales without code** — so content is data (`D-03`).
4. **A solo RPG must ship first** — so none of the above may become MMO engineering (`D-12`).

---

## 2. Layer model

```
┌──────────────────────────────────────────────────────────────────────┐
│  PRESENTATION  (Godot 4.x / C#)                    src/Presentation/ │
│  Scenes, nodes, cameras, UI, animation, VFX, audio                    │
│  • reads state snapshots        • submits commands                    │
│  • subscribes to events         • NEVER writes authoritative state    │
└───────────────┬──────────────────────────────────▲───────────────────┘
                │ Commands                         │ Events / snapshots
┌───────────────▼──────────────────────────────────┴───────────────────┐
│  APPLICATION  (plain C#)                           src/Application/  │
│  Command bus, event bus, system scheduling, save orchestration,       │
│  session lifecycle, config                                       │
└───────────────┬──────────────────────────────────▲───────────────────┘
                │ Mutations                        │ Domain events
┌───────────────▼──────────────────────────────────┴───────────────────┐
│  DOMAIN  (plain C#, ZERO Godot references)          src/Domain/       │
│  Systems own state. Registry owns identity. World state store.        │
│  Rules, formulas, validation. Deterministic. Headless-testable.       │
└───────────────┬──────────────────────────────────▲───────────────────┘
                │ Definition lookups               │
┌───────────────▼──────────────────────────────────┴───────────────────┐
│  CONTENT  (YAML → validated in-memory catalogue)    content/          │
│  Items, creatures, spells, quests, factions, loot tables, recipes     │
└──────────────────────────────────────────────────────────────────────┘
```

### The boundary is a compile-time boundary, not a convention

`src/Domain/Domain.csproj` and `src/Application/Application.csproj` **must not reference Godot**. Only `src/Presentation/Presentation.csproj` may. This matters because conventions erode and compile errors do not: a developer (or an AI session) who tries to reach for `Node` or `Vector3` inside a domain system gets a build failure, not a code review comment.

This is the single most important structural rule in the project.

---

## 3. Repository structure

```
G:\UNNAMED\
├─ docs/                          ← Phase 0 documents (this set)
│  ├─ PROJECT_CHARTER.md          (authoritative vision, do not edit)
│  ├─ PHASE_0.md                  (phase instructions)
│  ├─ DECISIONS.md                (ADRs, D-01..D-12)
│  ├─ ARCHITECTURE.md             (this file)
│  ├─ GAMEPLAY_LOOPS.md
│  ├─ SYSTEMS.md
│  ├─ DATA_MODEL.md
│  ├─ PERSISTENCE.md
│  ├─ WORLD_ARCHITECTURE.md
│  ├─ PROGRESSION.md
│  ├─ PROTOTYPE.md
│  ├─ VERTICAL_SLICE.md
│  ├─ ROADMAP.md
│  └─ RISK_REGISTER.md
├─ content/                       ← data, not code (D-03)
│  ├─ items/  resources/  creatures/  npcs/   spells/  abilities/  effects/  recipes/
│  ├─ quests/ dialogue/   factions/   loot/   merchants/  regions/  flags/  facts/
│  ├─ species/ schedules/ affixes/    sets/   nodes/  spawns/  locations/  config/
│  ├─ _tags.yaml                  ← closed tag vocabulary
│  └─ _aliases.yaml               ← definition-ID migration map (append-only)
├─ src/
│  ├─ Domain/                     ← NO Godot reference
│  ├─ Application/                ← NO Godot reference
│  └─ Presentation/               ← Godot project lives here
├─ tests/
│  ├─ Domain.Tests/               ← headless, fast, the bulk of testing
│  └─ Content.Tests/              ← content validation gates
├─ tools/
│  ├─ content-lint/               ← validates content/, exits non-zero on error
│  └─ quest-debug/                ← answers "what is this quest waiting on"
└─ UNNAMED.sln
```

**This tree is illustrative only for the directory *arrangement*; `DATA_MODEL.md` §1 is the authority for the content kind list**, which is closed and validator-enforced ("the validator fails the build on an unknown top-level content kind", `PROTOTYPE.md` §4.4). An earlier draft of this file showed a different set (`npc/`, `world/`, `status/`), which would have contradicted the kind table; the two now agree.

**Three projects, and the reason.** `Domain` and `Application` carry no Godot reference; only `Presentation` may. `Application` holds the command/event bus, system scheduling, save orchestration and session lifecycle — deliberately separate from `Domain` so that the *wiring* can be replaced (or split across a network boundary) without touching gameplay rules. Tests live under top-level `tests/`, not inside `src/`, so that `src/` is exactly the shipped projects. An earlier draft of `PROTOTYPE.md` put tests at `src/Domain.Tests/` and omitted `Application`; both have been corrected to match this section, which is the structure authority.

**Rationale for `content/` living outside the engine project:** content is not a Godot resource. Keeping it outside `src/Presentation/` makes it obvious that content outlives any engine choice, and keeps the engine from owning it.

---

## 4. Core runtime patterns

### 4.1 Command

A command is an intent, expressed as immutable data, submitted by presentation (or by AI, or by a system). Commands are **the only way state changes**.

```csharp
public readonly record struct MoveItemCommand(
    EntityId Actor,      // ULID of the acting entity
    EntityId From,       // container/actor
    EntityId To,
    EntityId Item,
    int Count,
    int RequestedSlot
) : ICommand;
```

Rules:
- Commands are **imperative, past-intent, and validated**: they describe *what the actor wants*, not what will happen.
- Commands may be **rejected**. Rejection is a normal outcome with a reason, not an exception. `MoveItemCommand` fails for insufficient count, missing container, full destination.
- Commands carry **no behaviour**. All behaviour lives in the handling system.

### 4.2 System

A system owns a slice of authoritative state and handles the commands addressed to it.

```csharp
public interface ISystem {
    // Registration hands each system the reader plus the internal writer. `IWorldStateWriter` is
    // internal to `Domain`, so `Application` and `Presentation` cannot name it at all (§5).
    void Register(ICommandBus bus, IEventBus events, IWorldState world, IWorldStateWriter writer);
    void Tick(SimulationContext ctx);   // optional; some systems are pure
}
```

Rules:
- **One system, one responsibility.** A system that needs to change another system's state does so by submitting a command, or by reading a published event — never by reaching into its state.
- Systems are registered in an explicit, ordered composition root. Order is data, not implicit discovery.
- Every system declares the **state it owns** in its own file header. This makes ownership auditable and prevents two systems quietly owning the same field — the most common way authority boundaries rot.

### 4.3 Event

An event is a **fact about something that already happened**, published after a successful command.

```csharp
public readonly record struct ItemMovedEvent(
    EntityId Actor, EntityId From, EntityId To, EntityId Item, int Count);
```

Rules:
- Events are **past tense** and **immutable**.
- Events are **not** commands. A listener must never respond to an event by mutating another system's state directly; it submits a command.
- Systems may publish and subscribe. Presentation may only subscribe.
- Events are **not persisted.** Anything that must survive a save is state, not an event. (Conflating these is a classic source of "it worked until we reloaded" bugs.)

### 4.4 World state

`IWorldState` is the authoritative store of *persistent* domain state: character records, inventories, quest states, faction standings, relationships, building records, and the sparse world delta. It is **not** a grab-bag: it is a keyed store of typed records partitioned by owner system.

Transient state (derived stats, cached pathfinding, animation state, spatial indices) lives **outside** world state and is rebuilt on load. See `PERSISTENCE.md`.

### 4.5 Data flow, end to end

```
Input → Presentation intent → Command
      → CommandBus → owning System validates
      → mutate WorldState (owned slice only)
      → publish Event(s)
      → Presentation subscribers update views
      → (if persistent change) mark cell/entity dirty for the save delta
```

Note the last step: **nothing writes to disk directly.** Persistence observes the dirty set and serializes on the save trigger.

---

## 5. State ownership

`PHASE_0.md` STEP 5 requires explicit authority boundaries. The rule is: **every piece of authoritative state has exactly one owning system.** If two systems need the same value, one owns it and the other reads it.

| Domain | Authoritative owner | Notes |
|---|---|---|
| Actor identity & existence | **Entity Registry** (`D-10`) | Registry owns lifetime and ID only — no gameplay rules |
| Attributes, derived stats | Character system | Base attributes persist; derived values are transient |
| Health / resources | Character system | Persists for named actors only |
| Player input intent | Presentation | Never authoritative; becomes a command |
| Position (actors) | Movement system | Persists only for named/persistent actors, and coarsely |
| Combat resolution | Combat system | Ephemeral encounters; results mutate persistent state |
| Damage application | Combat system | Sole writer of health |
| Status effects | Status-effect system | Persisted only when they outlive an encounter |
| Inventories & containers | Inventory system | Sole writer of item placement |
| Equipment slots | Equipment system | Submits commands to Inventory for moves |
| Item instances | Item system | Owns instance records; definitions are content |
| Crafting jobs | Crafting system | Consumes inventory via commands |
| Resource nodes / harvesting | Resource system | Respawn state is persistent per-cell |
| Buildings & pieces | Building system | Sparse delta, per-cell exceptions (`D-08`) |
| NPC identity & schedule | NPC system | Tiers per `D-06`, detailed in `WORLD_ARCHITECTURE.md` |
| NPC/creature AI (tactical) | AI system | Transient; destroyed at tier demotion |
| Companions | Companion system | Richer record; persists fully |
| Relationships | Relationship system | Directed values per (subject, object) |
| Quests | Quest system | Declarative state machines (`D-07`) |
| Dialogue runtime | Dialogue system | Transient; writes quest events |
| Factions | Faction system | Static definitions + dynamic standing |
| Reputation | Faction system | Derived from standings; not separately stored |
| Spawning | Spawn system | Budgeted population per cell/tier |
| Dungeons/interiors | World streaming | Instanced spaces; persistence via delta |
| Bosses | Spawn + Quest systems | Unique spawn records; death is persistent |
| Merchants & economy | Economy system | Stock, prices, gold sinks |
| Time & weather | World clock | Global, monotonic, persists |
| Discovery / map knowledge | Exploration system | Per-region discovery state |
| Fast travel | Travel system | Unlocked nodes only (`D-03` content-gated) |
| Save/load | Persistence service | Reads all owners; writes none of their rules |

### The rule that keeps this honest

> A system may mutate only its own slice. Everything else goes through a command.

When a system needs another system to act, it **submits a command** rather than calling a method that mutates foreign state. This is more verbose. It is also the difference between a codebase that can accept a server-authoritative change later and one that cannot.

#### How that rule is enforced, not merely stated

An adversarial review made the correct observation that §5's ownership table and this rule were **unenforceable as originally written**: `IWorldState` was handed whole to every system with an unguarded `Write<T>(id, value)`, so "callable only inside a tick drain" was a sentence rather than a boundary. The project enforces the presentation→domain seam with a compile error (§2) and then left the *interior* boundary — the one that "keeps this honest" — to discipline. That is the specific failure mode this architecture exists to avoid, so it is closed structurally:

1. **The store is write-inaccessible from outside the domain assembly.** `IWorldState` exposes read operations publicly; the write operations live on a separate interface, `IWorldStateWriter`, declared `internal` to `Domain`. Only types inside `Domain` can call `Write` — the same class of guarantee as the Godot-reference ban, and it costs nothing at runtime.
2. **Systems receive the writer through their `Register`, and nothing else does.** `Application` (wiring) and `Presentation` (views) can hold `IWorldState` and cannot reach `IWorldStateWriter` at all. A view that tries to write fails to compile; so does a composition root that tries to.
3. **Cross-slice writes *within* `Domain` remain convention**, and that residual is stated honestly rather than hidden. It is narrowed by making each system's owned slice explicit in a typed component key registered at composition, so `Write` on a component no system declared ownership of is a **startup error**, not a silent success. That converts the realistic mistake — writing a component nobody owns — into a boot failure.
4. **The architecture test in Milestone 1/2 asserts both**, before there is anything to violate: (a) `IWorldStateWriter` is not visible outside `Domain`, and (b) every component key is claimed by exactly one system.

What remains genuinely unenforced is a Domain system deliberately writing another Domain system's *component*. That is detectable only by review. It is recorded as a risk (`RK-15` in `RISK_REGISTER.md`) rather than claimed as solved.

#### Two-player-context disjointness

The same test suite carries a second invariant that is cheap now and impossible to retrofit: **two independent world instances in one process must not share mutable state.** Two `WorldState` instances, two registries, two sets of systems, all state reachable only through its own instance. This is the mechanical form of `D-12`'s extension point and it catches static/singleton state the moment it appears.

---

## 6. Avoiding the god object

The charter forbids "a god object or universal GameManager". Three structural defences:

1. **No `GameManager`.** There is no global singleton with broad responsibilities. Composition happens once, in a composition root, which is configuration rather than behaviour.
2. **The Entity Registry is deliberately narrow** (`D-10`). It creates, assigns IDs, and resolves. It owns no rules, no stats, no state beyond identity and lifetime. Changes to it are treated as high-risk.
3. **Ownership is declared and auditable** via the table in §5 and per-system file headers. A system that starts owning two unrelated concerns is a review failure.

The thing most likely to become a god object by accident is **WorldState** — because it is the thing everyone can see. It is a *store*, not a *system*: it holds records and offers typed access, and it contains no decisions. Any `if` statement inside it is a bug.

---

## 7. Threading and simulation discipline

Phase 1 is single-threaded for correctness. The architecture reserves, but does not implement, a schedule:

| Phase | Threading |
|---|---|
| 1–2 | Single-threaded domain tick. Presentation runs on Godot's main thread. |
| 3+ (only if measured) | Domain tick may move off the main thread; commands are queued across the boundary in both directions. |

Constraints that keep later parallelism possible without rework:
- The domain tick is **the only place lazy state evolves**. No system mutates state from a callback outside `Tick`.
- Commands are **the only cross-boundary payload**, and they are immutable value types.
- Snapshots handed to presentation are **read-only** or copied. Presentation never holds a live reference to mutable domain collections.

We do not build a job system now. We simply refuse to write code that makes one impossible.

---

## 8. Composition and boot sequence

**Two sequences exist and they are different.** *Boot* (§8.1) is what happens from process start and is owned by this document. *Load* (§8.2) is what happens when a save is applied and is owned by `PERSISTENCE.md`.

### 8.1 Boot (process start)

```
1. Load content/             → validate against schemas; fail loudly on error
2. Build ContentCatalogue     → immutable; definitions only
3. Create WorldState          → empty authoritative store
4. Create Entity Registry     → ULID generator anchored from the save (or fresh)
5. Register systems           → explicit ordered list from the composition root
6. World streaming begins     → assign initial cell tiers around the player
7. Load save, if any          → §8.2, in that order
8. Tick loop starts           → fixed timestep for simulation; variable for presentation
```

Failure policy at step 1 is **refuse to start**, with file, line, and reason. A content error that becomes a mid-quest null reference is far more expensive than a build that will not boot. This is also what makes `tools/content-lint` a CI gate rather than a convenience.

### 8.2 Load (NOT specified here — `PERSISTENCE.md` §7.4 is the sole authority)

**This section deliberately does not restate the load sequence.** An earlier revision published its own ordered list here, which made two documents normative for one procedure and let them drift: this section applied deltas *before* streaming regenerated the baselines those deltas are relative to, inverting `SYSTEMS.md` S-33 and `PERSISTENCE.md` §1.2.

The normative load sequence is **`PERSISTENCE.md` §7.4**, implemented as a gated state machine so an out-of-order call is a state error rather than a silent misbehaviour. Three properties of it are worth naming here because they are architectural rather than persistence-local:

- **It is one numbered sequence, and it is the only one.** `SYSTEMS.md` S-33 and this document both defer to it.
- **Derived caches are recomputed exactly once, after migration and alias resolution** (`PERSISTENCE.md` `I-6`). Recomputation attached to deserialisation produces stale numbers whenever a migration or alias pass changes an input, and the failure is silent because the number is plausible.
- **Partial load is legal for a quarantinable section, and refusal is legal for a container mismatch.** A corrupt `cells`/`entities`/`buildings` section loads without it, recording `flags.quarantined_sections` and stating what was lost, because partial recovery beats none. A `save_format` mismatch **refuses**, because a container layout cannot be guessed. `SYSTEMS.md`'s "fall back to a backup slot rather than partially loading" describes the refusal case only; both paths exist and are distinct.

The **order** is asserted by `PERSISTENCE.md` `T-27`, and the replay tuple by `T-28`. Cross-slice write ownership is enforced as described in §5.

---

## 9. Dependency direction

```
Presentation ──▶ Application ──▶ Domain ──▶ (reads) Content
      ▲                              │
      └────────── events ────────────┘
```

No upward dependency is permitted. Specifically:
- Domain never references Application or Presentation.
- Domain never reads a file to load content — it receives an already-built catalogue.
- Content never references code; it is referenced *by* string ID (`D-04`).

This keeps the domain testable in isolation and makes the engine genuinely replaceable, which is what makes the `D-01` revisit condition meaningful rather than rhetorical.

---

## 10. Testing strategy

The charter makes continuous testing of core systems mandatory. The layer split makes that affordable:

| Test kind | Where it runs | Covers |
|---|---|---|
| Pure domain unit tests | `dotnet test`, no engine | Inventory transfers, equipment, XP/leveling, crafting math, resource consumption, damage, status effects, quest predicates, relationship changes, faction reputation, loot rolls, item uniqueness |
| Persistence round-trip tests | `dotnet test` | Save/load, delta application, migration chain, corruption handling |
| Content validation | `tools/content-lint` | Broken definition references, schema violations, unreachable quest objectives |
| Headless engine tests | Godot headless | Scene wiring, resource loading, build smoke |
| Runtime validation | Manual / scripted session | Movement, combat feel, AI behavior, building placement |

The charter's test list (save/load, inventory transfers, equipment, death, XP, leveling, crafting, resource consumption, quest state, companion state, NPC state, building persistence, loot generation, item uniqueness, relationship changes, faction reputation) is **overwhelmingly domain-level**, which is precisely why `D-02` was chosen. Those tests must not require launching a game.

---

## 11. Explicitly deferred

Named here so that their absence is a decision rather than an oversight:

- **Networking of any kind.** No sockets, no replication, no dedicated server, no rollback (`D-12`).
- **Job system / multithreaded simulation** (§7).
- **Full ECS** (`D-02`, alternative 2).
- **Structural simulation for buildings** (`D-08`).
- **Visual scripting for quests** (`D-07`).
- **Editor tooling beyond content lint and the quest debugger.** Build tools when authoring actually hurts.

---

## 12. Cross-references

| Concern | Document |
|---|---|
| Gameplay loops and reward flow | `GAMEPLAY_LOOPS.md` |
| Per-system specifications | `SYSTEMS.md` |
| Content schemas and definition vs instance data | `DATA_MODEL.md` |
| Save format, migrations, corruption | `PERSISTENCE.md` |
| Cells, regions, simulation tiers | `WORLD_ARCHITECTURE.md` |
| Progression axes | `PROGRESSION.md` |
| Prototype and vertical slice scope | `PROTOTYPE.md`, `VERTICAL_SLICE.md` |
| Ordering and milestones | `ROADMAP.md` |
| Risks and the implementation handoff | `RISK_REGISTER.md` |
| Why any of this was chosen | `DECISIONS.md` |
