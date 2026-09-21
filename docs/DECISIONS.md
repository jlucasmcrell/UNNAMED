# DECISIONS.md — Architectural Decision Records

**Project:** UNNAMED (working title) — first-person, solo-first, open-world fantasy RPG
**Phase:** 0 — Vision and Architecture
**Status:** Draft for owner review
**Authority:** PROJECT_CHARTER.md is the authoritative creative vision. This file records *how* we implement it and *why*. Where this file and the charter disagree, the charter wins and this file is wrong.

## How to read this file

Each decision has an ID of the form `D-xx`. **These IDs are stable and citable.** Code comments, other documents, and future sessions should reference decisions by ID rather than restating them.

Every decision records: the decision, alternatives considered, why it was selected, consequences (including the bad ones), and the conditions that would justify revisiting it. A decision with no stated consequences is a decision we have not actually thought about.

The **Revisit if** field is load-bearing. It is the only sanctioned trigger for reopening a settled question. The charter says "do not repeatedly reopen the engine debate"; these trigger conditions are what makes that instruction enforceable rather than merely aspirational.

---

## D-01 — Engine: Godot 4.x (C#)

**Decision.** Build the game in **Godot 4.x**, using **C#** as the primary gameplay language.

**Alternatives considered.**

| Engine | Case for | Case against, for *this* project |
|---|---|---|
| **Unreal Engine 5** | Best-in-class first-person rendering; World Partition + Level Streaming built in; navmesh and Mass AI built in; MetaHuman and a vast asset marketplace; Lumen/Nanite give a small team AAA-adjacent visuals cheaply. | Blueprint visual scripting is a *hostile* artifact for coding agents — it is binary, not diffable, and not reviewable in a pull request. C++ is the alternative but iteration is slow. Project files are large and opaque. Licensing friction if revenue succeeds. Heavy hardware requirements for the *editor*, not just the game. |
| **Unity 6** | Mature C# ecosystem; excellent tooling; strong asset store; good first-person support; HDRP/URP options. | Scene/prefab serialization is YAML-with-GUIDs — technically text, but merge-hostile in practice. Repeated licensing/policy churn makes a 3-year bet uncomfortable. Historically the weakest of the three at shipping *large streaming open worlds* without significant custom work. |
| **Godot 4.x** | Fully open source (no licensing risk for a multi-year project); **plain-text, human-readable, diffable scene and resource formats** (`.tscn`/`.tres`); first-class C# support; small editor footprint that runs anywhere; permissive asset pipeline; trivial to script and to *test* headlessly. | Weaker built-in open-world tooling: no World Partition equivalent, no built-in crowd/Mass AI. Rendering ceiling is lower than UE5. We will write more infrastructure ourselves. |

**Why selected.** The decisive factor is the charter's explicit requirement that a coding agent be able to work effectively with the codebase, combined with the "continuity" requirement that another model can resume the project from the repository alone.

- Godot's scene and resource formats are **text**. An agent can read, diff, review, and generate them. Unreal's Blueprints cannot be reviewed by a model at all, which would push all game logic into C++ and destroy iteration speed.
- Godot's formats **merge** acceptably. For a project that will be edited by many sequential AI sessions, mergeability is not a nicety; it is the difference between a coherent codebase and a corrupted one.
- Godot runs and **tests headlessly** with a single binary, which makes automated validation of save/load, combat math, and inventory transfers — all required by the charter's TESTING section — genuinely cheap. This is the single highest-leverage property for AI-assisted development.
- No licensing exposure over a 3-year horizon.
- The rendering gap is real but **not decisive**: a tiny team assisted by AI will not out-art a studio regardless of engine, so the marginal value of UE5's renderer is lower than it first appears. The charter's stated creative goal — "the WORLD is the primary character", old-school exploration, readable combat — is achievable at Godot's fidelity.

**Consequences.**

- We must build or adopt: world streaming, LOD management, crowd abstraction, and layered simulation. These are designed in `WORLD_ARCHITECTURE.md` and are *our* responsibility, not the engine's.
- Visual ceiling is lower. We compensate with strong art direction and intentional design rather than raw fidelity. This is a deliberate trade.
- New contributors cannot rely on Unreal/Unity tutorials; Godot-specific knowledge is assumed.
- C# (not GDScript) for gameplay so that systems are statically typed, unit-testable outside the engine, and reviewable by tooling. GDScript is acceptable for editor tooling only.

**Revisit if.** Any of the following becomes true — and only then:
1. Godot cannot sustain the required first-person frame budget in a 2×2 km vertical slice *after* the streaming and LOD work in `WORLD_ARCHITECTURE.md` is implemented — i.e. we have measured, not guessed.
2. A required subsystem (e.g. large-scale crowd navigation) proves unbuildable in Godot within one milestone of effort and no viable plugin exists.
3. The project's art direction changes to require rendering fidelity that Godot demonstrably cannot reach.

Note the standard this sets: "Unreal would be easier" is **not** a revisit trigger. The engine choice was made with that objection already on the table.

---

## D-02 — Authority model: single-player, but server-shaped

**Decision.** The game runs locally, but **all authoritative gameplay state lives in engine-agnostic C# domain assemblies** that the Godot presentation layer *observes*. Mutations flow through a **command/event bus**, never through UI or scene objects writing state directly.

**Alternatives considered.**

1. **Direct scene-object authority.** Nodes own state; UI and gameplay scripts mutate nodes directly. Fastest to prototype, idiomatic Godot. Rejected: it is exactly the architecture that makes the charter's "single player → LAN co-op → dedicated server" path a rewrite. Saved state would be entangled with scene graph lifetime, and the charter explicitly forbids saving "fragile raw runtime pointers".
2. **Full ECS (e.g. Arch, Friflo).** Excellent data locality and cache behaviour at high entity counts. Rejected *for now*: it imposes a data-oriented mindset on every system, adds a large conceptual tax, and is optimized for a scale (tens of thousands of simultaneously-simulated actors) that a solo RPG will not reach. It also makes the code harder for a fresh model to reason about from documentation alone.
3. **Godot `Resource`-based state with signals.** Closer to idiomatic, but ties persistence to engine serialization and makes headless unit testing awkward.

**Why selected.** The charter asks for the *conceptual* client/server split without building a network server. A domain layer with explicit commands, events, and a single world-state store delivers exactly that, and it is also what makes the charter's mandatory tests (save/load, inventory transfer, XP, crafting, quest state) runnable as plain unit tests with no engine in the loop.

**Consequences.**

- **Cost:** more indirection than a quick prototype needs. A trivial "player picks up item" flow touches command → system → event → view. This will feel like overhead during Phase 1 and *must not* be abandoned because of that feeling; it is the entire point.
- **Benefit:** headless testability, clean save boundaries, and a credible networking path.
- All systems are **engine-agnostic C#** in `src/Domain/`. Only `src/Presentation/` may reference Godot types. This boundary is enforced by project structure (separate `.csproj` with no Godot reference) so that a violation fails to compile rather than merely being discouraged.
- No networking code is written in Phase 0–2. Only the *seam* exists.

**Revisit if.** Profiling shows the command/event indirection is a material frame-time cost in the vertical slice. Even then, the fix is to batch/optimize the bus, not to dissolve the boundary.

---

## D-03 — Content is data, not code

**Decision.** All content (items, creatures, spells, recipes, quests, factions, loot tables, dialogue) is defined in **human-readable YAML** under `content/`, validated at load time against C# schemas, and referenced by **string definition IDs**. No quest, item, or creature is hardcoded in gameplay code.

**Alternatives considered.** Godot `.tres` custom Resources (engine-coupled, GUID clutter, poor for hand-authoring thousands of entries); JSON (no comments — fatal for a project whose content authors are largely AI sessions that must leave design rationale inline); a database (overkill, and loses diffability); C# code-as-content (recompilation per content change, impossible to hand-author at scale).

**Why selected.** YAML supports comments, which is how we preserve *why* a piece of content exists next to the content itself. It diffs cleanly in git. It is trivially generatable by an AI session and trivially reviewable by a human. The charter's DATA-DRIVEN DESIGN section requires exactly this.

**Consequences.**

- Requires a **content validation pass** at startup and in CI. A typo in a definition ID becomes a load-time error with a file and line, not a null-reference crash mid-quest. This validator is a Phase-1 deliverable, not a "later" task.
- Requires content *tooling* to be pleasant or hand-authoring becomes miserable. Minimal tooling (lint, schema dump, ID cross-reference check) is scoped into `ROADMAP.md`.
- Startup cost of parsing YAML must be managed via a compiled cache for shipped builds.
- Hot-reload of content is a natural early win and is planned for developer builds.

**Revisit if.** Content volume makes YAML parsing a measured startup problem that a binary cache cannot solve, or authoring tooling proves unable to keep pace. Format change is cheap early and expensive late — this is why the decision is made now.

---

## D-04 — Identity: ULID instance IDs, dotted definition IDs

**Decision.** Two strictly separate identity namespaces.

- **Definition IDs** — human-authored, stable, dotted, lowercase, namespaced by kind: `item.weapon.iron_sword`, `creature.beast.wolf_grey`, `quest.artifact.shattered_crown.03`. These live in content files and **never change once shipped** (renaming requires a migration map).
- **Instance IDs** — generated at runtime as **ULIDs** (lexicographically sortable, collision-resistant, lowercase): `itm_01J8ZC4K9P...`. These identify a *specific* sword, NPC, building, or container in a save.

**Alternatives considered.** Integer handles (fast, compact, but require a central counter that must persist and can collide across save/merge); GUIDv4 (opaque, non-sortable, hostile to debugging and save diffing); raw object references (explicitly forbidden by the charter — "do not serialize fragile raw runtime pointers"); content definition path used as instance identity (breaks the moment two iron swords exist).

**Why selected.** ULIDs are sortable, which makes saves diffable and makes "most recently created" queries free. String IDs survive serialization without a lookup table and are legible in a save file when debugging — a property that matters enormously when an AI session must diagnose a persistence bug from a dump.

**Consequences.**

- Instance IDs are ~26 chars; at scale (tens of thousands of persisted entities) this is real save bloat. Mitigated by the sparse-delta save design in `PERSISTENCE.md` (D-05).
- Every persisted entity **must** carry its instance ID at creation. Entities created before an ID is assigned are a bug class; the registry (D-10) assigns IDs at spawn so this cannot happen.
- Definition IDs are a **public API**. We need a deprecation/alias mechanism before the first content pack ships. Recorded as a risk in `RISK_REGISTER.md`.

**Revisit if.** Save size becomes a measured problem that sparse deltas do not solve — in which case switch instance IDs to a save-local integer table with ULIDs retained as the authoring/cross-save key. This is a contained change *because* identity is centralized in the registry.

---

## D-05 — Persistence: sparse deltas over a deterministic baseline

**Decision.** A save is **not** a snapshot of the world. It is:
1. a small **manifest** (save version, content version, seed, playtime, screenshot, checksum),
2. **player/companion state** (small, fully serialized),
3. a **sparse world delta**: only entities and world cells that *differ from their deterministic baseline*.

**Alternatives considered.** Full-world serialization (simple, and balloons without bound as the world grows — the charter explicitly forbids "saving the entire runtime world blindly"); binary snapshot (fast, opaque, unmergeable, undebuggable); per-cell files always written (predictable, but writes megabytes of unchanged data).

**Why selected.** Baseline-determinism means the *unchanged* world costs zero bytes. A player who has explored 2% of the world writes a save proportional to that 2%, not to the world. It also gives us a clean definition of "world change": a cell is persisted only when it diverges.

**Consequences.**

- **Hard requirement:** world generation from `(seed, content_version)` must be **deterministic and version-stable**. This is the single most dangerous constraint in the project. If generation is not stable, deltas apply to a baseline that no longer matches and the world corrupts. Mitigations: generation code is frozen per content version, the seed is recorded in the manifest, and a content-version mismatch triggers migration rather than silent regeneration. Tracked as `RK-01` in `RISK_REGISTER.md`.
- Deleted/changed definitions between versions must be handled by a migration map, never by silent drops. Required by the charter's save-versioning clause.
- Save must be written **atomically** (temp file + rename) with at least one rolling backup slot, because corruption recovery is explicitly in scope.

**Revisit if.** Determinism proves impossible to hold across content versions for some subsystem. The fallback is to promote *that subsystem's* cells to full serialization while keeping deltas elsewhere — a local, contained degradation rather than a redesign.

---

## D-06 — World simulation: four tiers + a global tick

**Decision.** Everything in the world is assigned a simulation tier:
`Tier A` full simulation (nearby) → `Tier B` simplified regional → `Tier C` abstract schedule/economy → `Tier D` stored state only.

**Alternatives considered.** Simulate everything at full fidelity (impossible — the charter forbids it: "do not simulate every NPC and creature at full fidelity when they are kilometers away"); simulate nothing off-screen (world feels dead, and the charter requires NPCs to "appear to have lives"); a single global "catch-up" simulation (cheap but produces nonsense — e.g. every NPC converges on the same behavior).

**Why selected.** The charter's promise — "a world that exists independently of you" — is a *presentation* of continuity, not a simulation requirement. Tiered simulation delivers the felt result at bounded cost, and the tiers map directly onto the NPC tiers required by `PHASE_0.md` STEP 10.

**Consequences.** The hard part is **tier transitions**: an NPC promoted from C to A must arrive in a state consistent with what it was abstractly doing. This is where such systems usually break. Mitigation: abstract tiers advance **schedules and coarse position only**, never combat or inventory; on promotion, the NPC is reconciled to a legal state rather than having its abstract state naively adopted. Detailed in `WORLD_ARCHITECTURE.md`.

**Revisit if.** Players observe discontinuity at tier boundaries in playtest — the visible symptom is an NPC "teleporting" or being somewhere its schedule says it should not be.

---

## D-07 — Quest system: declarative objectives evaluated against world state

**Decision.** Quests are YAML **graphs of objectives**. Each objective has a `type` plus parameters, and is satisfied when a **predicate over world state** becomes true. The engine evaluates predicates; it does not contain per-quest logic.

**Alternatives considered.** Hardcoded quest scripts (charter explicitly forbids: "avoid hardcoding individual quests into gameplay code"); a visual scripting language (charter explicitly forbids: "without attempting to build a visual programming language"); simple kill-counter/flag systems (cannot express the charter's exploration, crafting, construction, faction, relationship, puzzle, hidden, timed, and world-state objectives).

**Why selected.** A predicate model generalizes to every objective type the charter lists *without* new engine code per quest. Adding "deliver 3 crafted silver ingots to a specific NPC at night" is content, not engineering.

**Consequences.** Predicate systems can become unreadable and un-debuggable if unconstrained. Mitigations: a closed set of objective types, each with a strict schema; a **quest debugger** that answers "what is this quest waiting on right now" — this is a Phase-1 tool, not a luxury, because we will be unable to diagnose long artifact quests without it. Long-running artifact quests (charter PHASE_0 STEP 11) depend on this system being correct early.

**Revisit if.** The closed type set repeatedly fails to express real quests and the workaround is engine code — that is the signal the abstraction is wrong.

---

## D-08 — Building: socket/snap assembly, explicitly not a structural simulator

**Decision.** Player building is **socket- and snap-based assembly** of authored pieces. There is **no structural integrity simulation, no physics-driven collapse, and no load-bearing calculation.**

**Alternatives considered.** Voxel building (deeply satisfying, enormous cost, changes the art direction, poor fit for authored fantasy architecture); free-form placement (flexible, produces ugly and buggy results, hard to guarantee navigability); full structural simulation (a second game we are not making — the charter says "do not design a structural-engineering simulator unless justified", and it is not justified here).

**Why selected.** Snapping guarantees that player-built structures remain **navigable** (NPCs and companions can path through them) and **reliable to persist** (a piece is a row in a table, not a physics resolution). Both properties are prerequisites for companions and settlement NPCs to function in and around buildings.

**Consequences.** Less creative freedom than voxel or free-form building; players cannot build arbitrary shapes. Mitigated by a generous, well-designed piece catalogue and free rotation/socketing rather than by loosening the structural model. "Damage" is a per-piece health value applied by explicit rules, not emergent physics.

**Revisit if.** Playtesting shows the snap catalogue is the actual limiter on player expression — the response is more/better pieces, not a physics engine.

---

## D-09 — Progression: orthogonal axes with distinct questions

**Decision.** Progression axes are permitted only if each answers a **different question**. The sanctioned axes are: character level (broad power), attributes (build shape), skills (learned capability), abilities/talents (active choice), weapon mastery (per-weapon-family proficiency), magic mastery (per-school proficiency), professions (crafting capability and quality ceiling), reputation (faction standing), equipment (immediate power), companions (party growth).

**Alternatives considered.** A single unified "power level" (simple, and destroys the charter's requirement that "build decisions should matter" and that players can develop "unusual combinations"); fully independent systems with no interaction (produces the failure the charter warns against: "five progression systems that all represent the same thing").

**Why selected.** The test "what question does this axis answer?" is falsifiable and reviewable. It caught two candidate axes during design — a separate "exploration level" and a "crafting level" distinct from professions — both of which were folded into existing axes rather than added.

**Consequences.** The interaction rules between axes must be explicit and documented in `PROGRESSION.md`, or we will drift into either duplication or an unscalable interaction matrix. Explicitly: level grants *breadth*, skills grant *competence*, masteries grant *specialization*, professions grant *production*, reputation grants *access*. A character cannot convert one axis into another.

**Revisit if.** Playtest shows two axes always move together — that is evidence they are one axis and should be merged.

---

## D-10 — Entity Registry as the single identity and lookup authority

**Decision.** A single **Entity Registry** owns creation, ULID assignment, and lookup of every runtime instance (items, NPCs, creatures, buildings, containers, quest instances). It is a *registry*, not a manager: it stores no gameplay state and contains no game rules.

**Alternatives considered.** Scattered static singletons per type (fast to write, produces untraceable lifetime bugs); no central registry with IDs assigned at persistence time (breaks the invariant that every entity has an ID from birth); a full ECS world container (see D-02).

**Why selected.** It is the smallest construct that guarantees D-04's invariant and gives save/load (D-05) a single place to enumerate persisted entities. Deliberately narrow scope prevents it becoming the "god object or universal GameManager" the charter forbids.

**Consequences.** Every system depends on the registry, so it must remain trivially correct and heavily tested. It must **never** grow gameplay responsibilities — that is the specific failure mode to watch. Changes to it are treated as high-risk.

**Revisit if.** The registry accumulates rules or becomes a contention point. The response is to split *lookup* from *lifecycle*, not to add features.

---

## D-11 — Presentation never mutates state

**Decision.** The Godot layer renders state and submits **commands**. It never writes authoritative state. Views subscribe to domain events; they do not poll and do not own truth.

**Alternatives considered.** Idiomatic Godot node-owned state (fast, and forfeits D-02); a two-way binding layer (convenient, and guarantees divergence between view and truth).

**Why selected.** It is the enforceable version of D-02. Without this rule, the domain/presentation boundary erodes one convenient shortcut at a time, and the charter's networking extension point quietly dies.

**Consequences.** Slight latency between input and visible result must be handled by **presentation-side prediction for feel only** (e.g. animation anticipation), never by writing state. This is the standard cost of server-shaped authority, paid now rather than at networking time.

**Revisit if.** Never expected to be revisited; violations are bugs.

---

## D-12 — Scope discipline: region-by-region, vertical slices, no MMO infrastructure

**Decision.** Build **one region completely** before building a second. Implement no networking, no dedicated server, no persistence format for concurrent multi-actor authority, and no speculative abstraction for futures that are not scheduled. The charter's tie-break rule is adopted verbatim: when "clean theoretical future MMO architecture" conflicts with "actually getting the single-player game working", **choose the working single-player implementation** while leaving the extension points named in these documents.

**Why selected.** The charter states it directly, and every decision above is designed to satisfy it without building MMO infrastructure. This decision exists to be cited *against* future scope creep.

**Consequences.** Some future multiplayer work will be genuinely harder than if we had designed for it now. That is the accepted trade. The four extension points we *do* preserve are: (1) state mutation only via commands (D-02), (2) stable identities (D-04), (3) sparse, versioned persistence (D-05), (4) tiered simulation (D-06).

**Revisit if.** Multiplayer becomes a scheduled milestone rather than a possibility.

---

## Open questions for the project owner

Only questions where no reasonable default exists are listed. Per `PHASE_0.md`, this list is deliberately short.

1. **Working title and setting temperature.** The charter mandates an original setting but does not fix tone (grim-dark / classic high fantasy / weird-ancient / bronze-age mythic). World *architecture* does not depend on this, but *content* does, and Phase-1 needs *some* flavor text. Default if unanswered: a **melancholic, ancient, low-magic-feeling high fantasy** tuned for "the world is the primary character". Reversible, and cheap to change while content volume is near zero.
2. **Target platform baseline.** Affects the streaming and LOD budget in `WORLD_ARCHITECTURE.md`, and it is the platform on which `RK-13`'s atomic-save-commit test must be run. Default if unanswered: **Windows desktop, 16 GB RAM, mid-range discrete GPU**, i.e. not a VR or console target in Phase 0–2.
3. **Visual fidelity target.** Whether "readable and atmospheric" (Godot-comfortable) is acceptable in place of AAA fidelity. Default if unanswered: readable and atmospheric, per D-01.
4. **The "while you were away" policy.** Raised by `RK-12` and by `D-06`'s offline catch-up. When a save is loaded after a long absence, world time has advanced and abstract tiers must converge. The *mechanism* is specified (`D-06`); the *policy* is not — how much elapsed time is honoured, whether it is capped, and whether the player is shown a summary of what changed in their absence. Default if unanswered: **honour a bounded catch-up window, clamp all abstract values to their authored `[min, max]`, and present no "while you were away" report in Phase 1–2.** Deferrable until settlement simulation exists (Phase 2+), and reversible at any point before then.

No other owner input is required to proceed. Questions 1–3 can remain unanswered through Milestone 1 without blocking anything; question 4 has no dependency before settlement simulation. Consult `READY FOR IMPLEMENTATION` in `RISK_REGISTER.md` for the consolidated summary and the first ten implementation tasks.
