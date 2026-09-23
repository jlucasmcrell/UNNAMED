# PERSISTENCE.md — Save Architecture (Phase 0)

**Project:** Otherreach (codename UNNAMED) — full-body third-person with seamless first-person zoom, solo-first, open-world fantasy RPG
**Status:** Normative, and implemented through M2b (`src/Persistence`, `src/World`; evidence in `M2_STATUS.md` and `M2B_STATUS.md`). M2b refined this document per `M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md`: **exact content identity and baseline procedural compatibility are related, but they are not the same thing** (§1.3, §6.4).

**Normative decisions:** `D-05` (sparse deltas over a deterministic baseline), `D-04` (ULID instance IDs, dotted definition IDs), `D-10` (Entity Registry owns identity), `D-02`/`D-11` (authoritative state is engine-agnostic C#; presentation never writes state), `D-03` (content is data), `D-12` (no speculative multi-actor persistence).

**Where this file and `PROJECT_CHARTER.md` disagree, the charter wins and this file is wrong.**

**Authority inside the persistence topic.** This document is the single normative authority for the **save container, the manifest, the section format, the write sequence, the load sequence, and corruption recovery**. Two things it deliberately does **not** own:

- **Content file formats** (including `_aliases.yaml` / tombstones) are owned by `DATA_MODEL.md` — see §6.3, which cites it rather than restating it.
- **The boot sequence** (content load, catalogue, registry creation, system registration) is owned by `ARCHITECTURE.md` §8.1. This document owns only the **load** half; `ARCHITECTURE.md` §8.2 cites §7.4 here.

> **Reconciliation note (Phase 0 final pass).** An earlier revision of this file was truncated: it retained §1–§5 but lost §6–§11, including the write sequence that its own tests referenced. The **load sequence** had also been written into `ARCHITECTURE.md` §8.2, which made two documents normative for one procedure. Both are corrected here: §6–§11 are restored, §7.1/§7.2 are the numbered sequences, and `ARCHITECTURE.md` §8.2 now cites §7.4 instead of duplicating it.

Reader assumption: another AI coding session implementing persistence from this document alone, in Godot 4.x with C# domain assemblies. Nothing here requires the engine; only `src/Persistence/` may touch the filesystem.

---

## 1. Purpose, scope, invariants

Persistence is designed **before** gameplay systems exist, because every system must be written against the save boundary from its first commit. Retrofitting persistence onto scene-owned state is the failure mode `D-02` exists to prevent.

In scope: container layout, section schemas, delta semantics, versioning/migration, corruption recovery, autosave, atomic write, backup rotation, the mandatory test list.

Out of scope in Phase 0: networking, cloud sync, save sharing, anti-cheat, console certification, engine-specific serialization.

### 1.1 Invariants (violating any of these is a bug, not a design choice)

- **I-1** A save stores **nothing** derivable from the deterministic baseline (the world seed and the generator contract, §1.3) unless it diverges from it; no content definitions, only definition **IDs**; no raw pointers, object references, memory offsets, or engine node paths (`D-03`, `D-05`, charter SAVE SYSTEM).
- **I-2** Every persisted instance carries a ULID assigned at creation by the Entity Registry (`D-04`, `D-10`).
- **I-3** Generation from the baseline tuple (§1.3) is deterministic. **Every saved delta names the baseline it was made against** (its cell's `baseline_hash`) and is applied only to a baseline with that hash, or through a registered transition (§6.4); a mismatch is migrated or refused, never silently regenerated (`D-05`).
- **I-4** State is written only via domain commands; the save system reads the world-state store, never the scene tree (`D-02`, `D-11`).
- **I-5** A write is atomic: either the previous save or the complete new save exists, never a half-written one (`D-05`).
- **I-6** **Derived caches are recomputed exactly once, after migration and alias resolution, and never incrementally during load.** This is a sequencing invariant: a derived value computed from a half-migrated world is wrong in a way that no later step repairs. Enforced by **T-27** (order assertion) and by the gated load-state machine in §7.4.
- **I-7** **Every persisted record has a defined retirement path.** Presence in the delta implies divergence *from a regenerated baseline*, and a record that has returned to baseline is **rebased** (deleted) rather than retained. A delta format without compaction is a leak with a schema (`RK-P09`).
- **I-8** **Every persisted world entity carries a derived baseline slot key in addition to its ULID**, so a persisted entity *replaces* its baseline slot on load instead of coexisting with it (`RK-P10`).
- **I-9** **Randomness is derived, not sequenced.** A random value that must be reproducible is a pure function of its key: `Random(world_seed, cell, subsystem, semantic_key, sample_index)` for generation (`WORLD_ARCHITECTURE.md` §3.4, RNG contract 2) and `hash(world_seed, purpose_key)` elsewhere. There is **no global RNG counter in the save**, and **content identity is never an input to a random draw** (`RK-P11`, `RK-P14`).

### 1.2 The one-line model

> Load = `generate(seed, generator contract) + prove(delta) + apply(delta) + apply(player_state)`. Save = `diff(store, baseline(seed, generator contract)) + player_state + manifest`.

Content enters the model through definition IDs, which the load resolves (§6.3), and through the placement data the generator reads, which is part of the generator contract. The rest of the content pack does not move the baseline.

`diff` is only half-implemented by storing a delta. The other half is **`rebase`** (§5.6): the saved record is the only copy of the divergence, so nothing retires it unless an explicit reconciliation against the regenerated baseline runs. `rebase` ships **before** the delta path is considered complete.

If a piece of state cannot be expressed in that equation, it does not belong in this project's save.

### 1.3 The canonical determinism tuples

This project has exactly two tuples, and every other document cites them rather than restating them:

| Tuple | Fields | Used for |
|---|---|---|
| **Baseline tuple** | `(world_seed, generator contract)`: `worldgen_version`, `rng_contract_version` and the placement data the generator reads | Regenerating world/cell content exactly |
| **Replay tuple** | `(world_seed, generator contract, content_hash, command_log)` | Reproducing **tick outcomes** from a save |

**Content identity is in the replay tuple and not in the baseline tuple.** `content_hash` answers "what exact content pack wrote this save?": simulation reads content (a wolf's hit points), so replay needs it. Generation reads only its placement data. M2 fed the whole `content_hash` into every cell's random streams, so a one-value balance edit moved every rock and every wolf. M2b removed that coupling (`RK-P14`, `M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md` §2).

Three identities describe the generator, and each has one job:

- `worldgen_version`: the human compatibility epoch, bumped deliberately. Never the only safeguard.
- `worldgen_fingerprint`: computed from the generator's identity, version, RNG contract, placement data and its **output on a fixed set of canonical probe cells**, so drift that nobody versioned becomes visible (§6.4). It is detection metadata, never procedural entropy.
- per-cell `baseline_hash`: the digest of one cell's generated output, recorded with every changed cell's delta. It is the **authority** for applying that delta (§6.4).

`SYSTEMS.md` §1 states the replay tuple; §6.1 and §6.4 below state how a mismatch is handled.

---

## 2. What is NOT saved, and how it comes back

The heart of `D-05`. Every row states the reconstruction path, because *"not saved" only means "can reconstruct mechanically"*.

| Not saved | Reconstructed at load by |
|---|---|
| Unchanged terrain, heightfield, biome, water, roads | Deterministic worldgen from the baseline tuple (§1.3) |
| Unchanged vegetation, props, decals | Same generator, semantic random channels keyed by cell, subsystem and slot (`WORLD_ARCHITECTURE.md` §3.4) |
| Content definitions (items, creatures, spells, recipes, quests, factions, loot tables, dialogue) | Content loader + validator; only definition **IDs** were stored (`D-03`) |
| Unmodified creature/NPC instances at spawn defaults | Spawner re-instantiates from the cell's population table **minus persisted slot keys** (§5.3) |
| Unharvested resource nodes | Node existence/content from worldgen; baseline state is "full" (harvestable) |
| Navmesh, collision, occlusion, LOD meshes, GPU resources | Baking / streaming pipeline |
| Pathfinding, AI blackboards, animation state, ragdoll, physics contacts | Fresh instantiation on tier promotion (`WORLD_ARCHITECTURE.md` §7.3) |
| Presentation-only state (camera, HUD layout, particles, subtitles) | Presentation defaults + optional `client_prefs.json`, not part of the save (`D-11`) |
| Derived caches (encumbrance, faction power, settlement wealth) | Recomputed **once**, after every migration and alias resolution (§7.4 step g); if cached on disk, marked `derived: true` and discarded on any doubt |
| Quest progress index | Recomputed from the journal and the persisted quest instances |
| Offline time/catch-up | `world_time_advance(delta)` — a **pure function of total `tick_delta`**; the honoured amount is persisted (§5.5), never a load-time policy |
| Per-cell **dirty flags** | Recomputed at save time by diffing against the regenerated baseline; the flag is a performance hint, not the source of truth (§5.2) |

**Honest risk.** A *missed* divergence silently loses world changes while the save looks valid. Mitigation: one mutation dispatcher owns the hint flag (`RK-P02`), and the **authoritative** dirty set is derived at save time rather than trusted from the flag, so a missed flag costs performance, not data.

---

## 3. Save container

### 3.1 Shape: a directory (never a zip or a single file)

The save **is a directory** with one file per section plus two metadata files. Shipped builds *may* store the same tree as a zip with `manifest.json` as the first entry so tooling can open it without unpacking, but the domain layer always writes it as a directory. That is because:

- Sections are written, validated, replaced, and recovered independently
- A human or AI session can `find` a save and list its contents
- A section can be rewritten per-cell or per-region without rewriting the entire save
- The cost of corruption is limited to one section, not the whole save

### 3.2 Layout

```
saves/<profile>/<slot>/  # <slot>: quick, manual_<slug>, auto_NN, or pre_migration_<schema>_<slot>
  manifest.json             # human-readable JSON; the only file a tool needs first
  player.msgpack            # fully serialized: character, inventory, equipment, skills, quests, reputation
  companions.msgpack        # fully serialized: companion roster and rich state
  entities.msgpack          # sparse entity delta (world-scoped instances), slot-keyed (§5.3)
  cells.msgpack             # sparse cell delta (per-cell flags, node/spawn overrides)
  buildings.msgpack         # player structures and their pieces
  journal.jsonl             # append-only human-readable log of the session that produced this save
  command_log.jsonl         # VERSIONED, PERSISTED, REPLAY-TESTED (see §5.7) — not a debug artifact
  orphans.msgpack           # OPTIONAL — created only on a quarantine event (§7.2)
  sections.sha256           # section → SHA-256; the integrity root; never the whole save
  preview.png               # optional slot-UI screenshot (not authoritative, not checksummed)
```

**Where `<profile>` lives is part of the contract, not an implementation detail.** The save root must be a location **excluded from known cloud-sync roots** (OneDrive/Dropbox), or the game must detect and warn. Windows user-profile redirection into OneDrive is common, and a sync engine that hydrates placeholders or resurrects a deleted `staging`/`.trash-*` directory defeats the commit protocol (`RK-P06`).

`orphans.msgpack` is listed here rather than only appearing at the quarantine site, because §7.2 can create it and `savetool repair` can delete it, and a file that exists but is not in the layout is a file whose checksum is never written.

### 3.3 Section semantics

| Section | Why compact | Why not compressed | When corrupted |
|---|---|---|---|
| **`manifest.json`** | The slow path to determinism; human-readable and machine-readable | If corrupted the player cannot load a save at all | Hard error; never a load. `quicksave` must not accept a new manifest |
| **`player.msgpack`** | The player is the most fragile thing in the world; fully persisted | If corrupted the player dies permanently | Fatal error; the session cannot be recovered |
| **`companions.msgpack`** | Rich per-companion state is not regenerable (affinity, injury, personal-quest flags) | Loss is a permanent character regression | Fatal for that companion: quarantine the individual record and report which, never the whole roster |
| **`entities.msgpack`** | Sparse, slot-keyed divergence only (§5.3) | Linear in diverged instances, so a large world is a large section | Load **without** it, record `quarantined_sections`, and state what was lost (§7.2) |
| **`cells.msgpack`** | Sparse per-cell overrides only | Same linear argument | Load **without** it (§7.2) |
| **`buildings.msgpack`** | Regenerable from nothing — a player structure is authored state | Loss is unrecoverable player work | Load **without** it and report every lost structure explicitly; never fail the whole load |
| **`journal.jsonl`** | Append-only prose, useful to players and tools | It is not authoritative state and never replayed | Truncate to the last complete line; a partial tail line is not corruption |
| **`command_log.jsonl`** | A few KB; the enabler of the replay tuple (§1.3) | Loss makes a save unreplayable, not unloadable | Truncate to the last complete line; the save still loads, and the unavailability of replay is reported |
| **`orphans.msgpack`** | Records quarantined by the tombstone pass | Absent unless a quarantine occurred | Not load-bearing; a corrupt orphan file is reported and discarded |
| **`sections.sha256`** | The integrity root covering every other file including `manifest.json` (the `sha256sum` layout) | If corrupted, re-derive by re-hashing the directory | Re-derive and report it. The load then does not count as complete, so the save is not proven good for backup rotation (§7.3). The manifest records no sizes to check against (§4.2); an earlier revision referred to them |
| **`preview.png`** | Slot-UI affordance only | Not authoritative, not checksummed | Ignore and regenerate |

---

## 4. Manifest semantics

### 4.1 Purpose

`manifest.json` is the deterministic **load key**. It must carry **every field the normative load sequence (§7.4) reads before player state is deserialised**: the format version, the state-schema version, the content identity, the generation identity, and the honoured time advance. Anything the load path branches on and is *not* here is a field the load path cannot read.

### 4.2 Schema

```jsonc
{
  "save_format": 1,                       // CONTAINER version; never changes for small layout edits
  "schema_version": 3,                    // GAMEPLAY STATE schema; drives the migration chain
  "content_version": "0.4.2",             // content pack version; a human label
  "content_hash": "sha256:9f3c…",         // exact identity of the compiled content pack; NOT a generation input
  "world_seed": "0x5C1A9E7B4D2F0083",     // frozen; 0 means random
  "worldgen_version": 2,                  // human compatibility epoch of the generator contract
  "worldgen_fingerprint": "sha256:9810…", // computed generator identity incl. canonical probe output (§6.4); detection only
  "rng_contract_version": 2,              // the key-to-random-value encoding (WORLD_ARCHITECTURE.md §3.4)
  "world_tick": 918273645,                // monotonic domain tick; the only clock (§5.5)
  "world_time_advanced_ticks": 1728000,   // honoured offline advance; PERSISTED, not a load policy
  "command_log_sha256": "sha256:c7d2…",   // digest of command_log.jsonl; absent ⇒ replay unavailable
  "build_timestamp": "2026-02-14T22:31:07Z",
  "playtime_seconds": 41337.5,
  "flags": { "quarantined_sections": [] } // populated by §7.2 when a section is dropped
}
```

**Deliberate omissions and substitutions:**

- **No `rng_state` block.** Randomness is derived, not sequenced (`I-9`, `RK-P11`). The previous four-counter block is removed; a global counter would make correctness depend on load order and draw order being identical forever.
- **No checksum of the manifest itself.** The integrity root is `sections.sha256`, which covers every other file including `manifest.json`. A self-referential checksum is a trap (`DATA_MODEL.md` §5, `RK-P09`).
- **No generator field is the authority for applying a delta; each changed cell's `baseline_hash` is (§6.4).** A hand-edited integer cannot detect a generation change, and a whole-generator identity cannot say *which* cells a change touched. `worldgen_fingerprint` detects drift; the per-cell hash decides.
- **Schema 1 (M2) recorded `worldgen_digest`** (generator identity + placement data). Schema 2 replaced it with `worldgen_fingerprint`, which covers the same inputs plus the RNG contract and the canonical probe output, and added `rng_contract_version`. The 1 -> 2 migration checks the old digest before it trusts a schema-1 save's baseline (§6.2). Other M2 field names were kept.
- **`world_time_advanced_ticks` replaces a per-load catch-up cap.** The honoured amount is *persisted state*, so one 300-day step and 300 one-day steps agree (§5.5, `RK-12`).

---

## 5. Sections

Each subsection defines a section's persisted shape and the reconstruction path. Section names match §3.2 exactly.

### 5.1 `player.msgpack` / `companions.msgpack` — fully serialized

These are the only fully-serialized sections. A character is not regenerable, so nothing here is a delta. Includes: identity (ULID, name, species ref, appearance seed, archetype), the XP/skill/attribute ledgers, unspent points, equipment slots and per-instance state, inventory contents by ULID, quest instances, faction reputation and crime records, relationship values and the memory log, discovered-location records, and per-axis advancement telemetry.

**Rebase does not apply here.** There is no baseline to return to, so every field is authoritative and T-01 asserts full equality after reload.

Implemented so far (schema 3): the character's ULID, name, position in integer millimetres, `appearance_seed` (required from schema 3), and inventory stacks by item ULID. The rest of the list above arrives with the systems that own it.

### 5.2 `cells.msgpack` — sparse cell delta

Written **only** for a cell that diverges from its regenerated baseline.

| Field | Meaning |
|---|---|
| `cell_key` | Canonical cell address (`WORLD_ARCHITECTURE.md` §3); the section's key |
| `baseline_hash` | The digest of the exact cell baseline this delta was made against (schema 2+). The delta is applied only to a regenerated baseline with the same hash (§6.4) |
| `dirty_reasons` | Enumerated: `nodes`, `spawns`, `flags`, `entities`, `buildings`, `terrain` |
| `flags` | Cell-level world flags |
| `overrides` | Per-cell authored-value overrides (the documented re-roll path) |

**The authoritative dirty set is derived at save time; `dirty_reasons` is a hint.** The distinction matters in both directions (`RK-P02`):

- A **hint flag** that is set for a net-zero mutation (pick up an item and put it back) costs a wasted diff, not a wrong save.
- A **hint flag** that is *missed* — because the change did not pass through the dispatcher — costs nothing, because the save-time diff still finds the divergence by comparing against the regenerated baseline.

A save that trusted the flag alone would be monotonic in *commands issued* rather than in *world state*, and would grow without bound. This is why the flag is demoted to a performance hint and the diff is mandatory.

### 5.3 `entities.msgpack` — sparse entity delta, slot-keyed

Only entities that differ from their spawn baseline appear here. **Each record carries a derived baseline slot key as well as its ULID** (`I-8`):

| Field | Meaning |
|---|---|
| `instance_id` | ULID (`D-04`); the only cross-reference key used anywhere |
| `slot_key` | **Derived**: `<cellkey>.<population_id>.<ordinal>` |
| `generation_seq` | Monotonic per slot; distinguishes successive occupants of one slot |
| `def_id` | Definition ref, resolved through aliases at load (§6.3) |
| `dirty_mask` | Which fields differ from the spawned baseline |
| `state` | The diverged fields themselves |

**Load is a merge keyed on `slot_key`, not an append.** A persisted entity *replaces* the baseline slot it names. Without this, a killed generic NPC resurrects: the entity delta says "this slot is dead" while the spawner independently re-instantiates the population member, and both records are internally well-formed so no integrity check catches it (`RK-P10`, `RK-A8`).

A slot whose persisted state has returned to baseline is **rebased** (§5.6), not retained.

**Baseline proof.** A slot is part of its host cell's baseline, so the section also records the `baseline_hash` of every host cell it references (the `baselines` table, schema 2+), and a record is merged only into a host cell whose regenerated baseline has that hash (§6.4).

**Created instances (schema 3).** A persistent instance that no baseline slot generates - a dropped item, a placed chest - is stored whole in the section's `created` list: `instance_id`, `def_id`, `host_cell`, and position. It is proven against its host cell's baseline like a slot record.

### 5.4 `buildings.msgpack` — player structures

A building is a ULID-keyed structure with a footprint of one or more cells; pieces are ULID-keyed rows with a `def_id` and a socket path (`WORLD_ARCHITECTURE.md` §10). Nothing here is regenerable: a player structure exists nowhere else, so corruption is unrecoverable rather than merely annoying, and the quarantine path must name every lost structure.

`storage` entries reference container ULIDs that live in `entities.msgpack`. That cross-section reference is exactly the case §7.2 must handle when `entities` is quarantined: a dangling reference whose target section was dropped is **reported loss**, not a corruption, and must not be re-quarantined as if the reference itself were invalid.

### 5.5 Node, spawn, and time-derived state

**Node keys are derived, not stored ULIDs**, because a resource node is a property of the world rather than an instance the player owns: `node.<cellkey>.<rule>.<ordinal>`, where `<rule>` is the placement rule's semantic name and `<ordinal>` is the node's two-digit ordinal within that rule (schema 2+). That is what lets a pristine cell cost zero bytes. M2's keys were `node.<cellkey>.<index>`, a position in the generator's output list, so adding a rule re-keyed every later node; the 1 -> 2 migration maps them (§6.2).

Stored state per harvested node: `state`, `last_harvest_tick`, `harvest_seq`. The ready time is:

```
ready_tick = last_harvest_tick + respawn_window(node_def) ± jitter(hash(node_key, harvest_seq, world_seed))
```

Time not played still advances world time (abstract catch-up), so a node harvested before a session break is correctly regrown on return.

**Catch-up is a pure function of the total `tick_delta`, and chunking is unobservable.** The honoured advance is persisted as `world_time_advanced_ticks` in the manifest:

```
world_time += min(total_tick_delta, honored_advance)
```

Because the honoured amount is stored state rather than a per-load policy, **one 300-day step produces the same world as 300 one-day steps**. A per-load cap would make world state a function of *absence history* rather than of `(state, tick)`, and would silently contradict the pure-function property this section depends on (`RK-12`, `RK-P05`). Chunk size is an implementation detail and must never be observable in the result.

**Content tuning that feeds a persisted value is baseline-locked.** `respawn_window(node_def)` is a *content* value in a persisted computation, so a change to it moves every stored ready time. Content is therefore split into **shape** (definitions, renames, additions — handled by the alias pass) and **tuning** (coefficients a persisted value was derived from). A tuning change requires a schema/migration step, never the `content_hash` fast path (`RK-P12`).

**Open problem (recorded, not solved).** If respawn should later depend on *world* state (regional depletion, faction control), the dependency must be enumerated here before implementation, because it changes `ready_tick` from a pure function of persisted data into one that reads other sections in a defined order.

### 5.6 `rebase` — the retirement path

**`Rebase(cell | entity)` is a first-class persistence primitive**, not an optimization. A saved record is the only copy of its divergence, so without reconciliation a node harvested and regrown 400 hours ago, a door opened and closed, and a corpse long past decay are all retained forever.

| Aspect | Rule |
|---|---|
| **Trigger** | Cell unload; autosave; explicit compaction pass |
| **Outcome** | Regenerate the baseline, diff the record against it; if the difference is below epsilon, **delete the record** |
| **Scope** | Per cell and per entity slot, independently |

`T-03`'s delta-minimality assertion is written against this operation: after a full baseline-equivalence cycle (mutate, then revert), the section must **shrink** back to byte-identical. A delta format without compaction is a leak with a schema (`RK-P09`).

**Cross-cell divergence.** An entity whose *host cell* is pristine — a placed chest, a node mined in a cell never entered, a corpse dragged over a boundary — is stored as an entity record with its `slot_key` naming its host cell, and does not imply a cell record. Load applies entity records independently of whether a cell record exists, so the entity is neither dropped nor orphaned.

### 5.7 `command_log.jsonl` — the replay enabler

**The command log is persisted for every save, and replayed by a test.** It is a few KB. It is **not** a diagnostic-only artifact, and any statement to the contrary elsewhere is superseded by this section.

- **Format:** append-only JSONL, one command per line, with the tick it was dispatched on.
- **Scope:** every command that mutated authoritative state, in dispatch order.
- **Digest:** `command_log_sha256` in the manifest, so a truncated or edited log is detectable.
- **Test:** `T-28` feeds a saved log into a fresh world and asserts digest equality against the original final state. This is the mechanism that makes the **replay tuple** (§1.3) true rather than aspirational.

Events are **not** logged; they are derivable from commands plus the deterministic system order.

---

## 6. Versioning and the migration chain

### 6.1 Which version means what

| Field | Regime | Bump when | Load behaviour on mismatch |
|---|---|---|---|
| `save_format` | Monotonic integer | The **container** layout changes incompatibly | **Refuse to load.** Guessing a container layout is not recoverable |
| `schema_version` | Monotonic integer | **Gameplay state** shape changes | **Migration chain**, one version per step |
| `content_version` | Human label | Each content release | None: diagnostic and reporting only |
| `content_hash` | Computed hash of the exact content pack, alias map included | Any content pack change | The definition-ID pass (§6.3). **It never moves the baseline**, because it is not a generation input |
| `worldgen_version` | Monotonic integer, human-controlled | The generator contract changes on purpose (class D below) | A registered worldgen migration, or **refuse** |
| `rng_contract_version` | Monotonic integer | The key-to-random-value encoding changes | Part of the generator contract: as `worldgen_version` |
| `worldgen_fingerprint` | Computed (§1.3) | Anything that changes generator output or placement data | Reported. Changed cells are then decided per cell by `baseline_hash` |
| `baseline_hash` (per changed cell) | Computed digest of the cell's generated output | That cell's baseline changes | Equal: apply the delta. Different: a registered transition (§6.4), or **refuse**. Never apply a delta to a different baseline |

**Every changed cell's `baseline_hash` is verified before its delta is applied.** This is the correction M2b made to the M2 design, where one whole-generator digest decided for every cell at once. A generation change that does not bump a human-edited integer still changes the affected cells' hashes, so their deltas cannot be applied silently. An unrelated change leaves the other cells' hashes equal, so those cells load as before (`RK-01`, `RK-P01`).

**`content_hash` is exact identity, not a migration trigger for tuning.** An equal hash means no definition-ID pass is needed. A differing hash runs the pass, plus the chain if `schema_version` also differs. But a *tuning* change (§5.5) is not a rename and the pass cannot see it, so content that a persisted derived value was computed from is **baseline-locked** and requires an explicit migration step (`RK-P12`).

**Content change classes (M2b §5).** A content hash mismatch is not one thing:

| Class | Example | What happens |
|---|---|---|
| **A** Content-only, baseline-neutral | A wolf's hit points 30 -> 31 | `content_hash` changes; every `baseline_hash` stays equal; the save loads with no reshuffle |
| **B** Definition identity | `creature.wolf` renamed, or a definition removed | The alias map resolves it (§6.3); an unmapped ID refuses, naming the ID |
| **C** Baseline-affecting content | A spawn table or placement rule changes | Only the affected outputs change; changed cells whose hash differs need a registered transition (§6.4) |
| **D** Generator contract | A new terrain algorithm, a new RNG encoding | `worldgen_version` bumps, the fingerprint and probe digests change; affected deltas need a registered path, or the load refuses |
| **E** Schema only | Player state gains a required field | The ordered chain (§6.2) |

**Content-version numbering:** monotonic integer carried as a string (`"0.4.2"` is display; comparison is on the recorded integer). `DATA_MODEL.md` requires only that it is a single value recorded in both the save manifest and the compiled content cache; this document fixes it as monotonic so load-time relationship tests are decidable. `schema_version` is deliberately **not** named `save_version` — the two are different fields with different failure modes (`DATA_MODEL.md` §6).

### 6.2 The migration chain

**Migrations run on a copy, never in place.** The chain runs in memory on the save's decoded sections. The migrated save reaches disk only through §7.1's staging, verification and recovery - the same path as every save - so an interruption leaves the original or the complete migrated save, never a partial one. The original is kept as `pre_migration_<schema>_<slot>` (§7.3).

Each migration is a pure function `SaveDocument(n) → SaveDocument(n+1)`, registered in a single ordered table (`SchemaMigrations.Production`), one step per version. It reads version `n`'s frozen section shapes and writes version `n+1`'s. A gap in the table refuses; a version is never skipped. A step must not resolve definition IDs: it treats them as opaque strings, and the definition-ID pass runs once, on the current shape (§7.4). A step that cannot resolve something reports a blocker, never a guess.

**Implemented chain.**

| Step | What it does |
|---|---|
| 1 -> 2 | Worldgen 1 -> 2. Regenerates worldgen 1 frozen (after checking the save's `worldgen_digest`), maps each index-keyed node to its rule and ordinal, rebases every record onto worldgen 2 by semantic identity, and records each cell's `baseline_hash`. A target with no worldgen-2 counterpart is dropped as reported loss |
| 2 -> 3 | The player gains the required `appearance_seed`, derived from the player's ULID for older saves. The entities section gains `created` instances; older saves have none |

**Historical fixtures (M2b §11).** Every schema version that has shipped has a committed fixture written by that version's own writer (`tests/Persistence.Tests/Fixtures/`, policy in its README). CI loads every fixture under the current code, and migrates every one through the commit path, to its committed expected current state. A schema bump without a fixture, a chain step, or an updated expectation fails CI.

```csharp
// Values derived from 'might' are NOT recomputed here: they are caches, and the domain layer
// recomputes them after load. Never migrate a cache — that is how stale numbers travel.
```

**Scenario A — split a derived stat.** `schema_version 14 → 15`. Players have one `might` stat; design splits it into `strength` (melee power) and `endurance` (carry, stamina). The migration writes both new fields. Values *derived* from `might` are caches and are recomputed after load (§7.4 step g), not migrated.

### 6.3 Alias and tombstone resolution

Every stored `def_id` is resolved through the content alias map at load. **`DATA_MODEL.md` §2.1 owns the file format and schema** (`content/_aliases.yaml`, an append-only `aliases:` / `removed:` map); this document owns only the *load-time behaviour*:

| Alias outcome | Behaviour |
|---|---|
| **Hit in `aliases`** | Rewrite the ID to its current value and continue |
| **Hit in `removed`** | Apply the recorded disposition: `convert`, `destroy`, or `quarantine` |
| **No hit at all** | **Hard content error** naming the ID. Never a silent drop |

Dispositions for a removed definition:

1. `convert` → rewrite to the replacement ID (`removed: old: new`).
2. `destroy` → remove the reference and report it as loss (`removed: old: ~`); any quest objective referencing it is marked `failed: content_removed`, never silently completed. It is declared in content and reported by every load and dry run, so it is never a silent drop.
3. `quarantine` → move the record to `orphans.msgpack` (§3.2) and record the reference disposition of anything pointing at it, so a quarantined record is never a dangling reference. **Not implemented yet:** it needs `orphans.msgpack`, which arrives with the first system whose records can be orphaned.

Renames and replacements may chain. A cycle, or a chain that ends at no defined ID, is unresolved.

**Ordering.** The definition-ID pass runs **after** the schema chain and **before** baseline proof and invariant validation (§7.4). It needs the current shape to find every stored ID, so migrations treat IDs as opaque. It must precede validation: if validation ran first, every renamed ID would quarantine player data that a rename would have resolved, which is the single most likely silent data loss in the whole load path.

### 6.4 Baseline compatibility (M2b)

**A save is never applied to a baseline it was not proven compatible with.** Per changed cell:

1. **Prove.** Regenerate the cell and hash it. Equal to the saved `baseline_hash`: apply the delta. Proof covers cell records, the host cells of entity records, and the host cells of created instances.
2. **Or rebase through a registered transition.** A `BaselineTransition` names the exact `worldgen_fingerprint` the save was written with and the running one, so it applies only to the change it was written and tested for. Its rebase is conservative (M2b §7). A record is carried only when its target keeps a stable semantic identity in the new baseline: the same node key, the same population with the stored count inside its budget, the same slot of the same family. Values are carried, never recomputed. A record whose target vanished blocks the load, unless the transition declares that loss, in which case it is reported.
3. **Or refuse.** Name every mismatched cell and the fingerprints a transition would need. Never regenerate and apply the old delta anyway.

A cell nobody changed has no record and simply uses the current baseline.

**Drift detection.** `worldgen_fingerprint` includes the digests of four canonical probe cells under a fixed seed, and CI pins those digests (and the fingerprint) against an independent implementation. A generator edit that nobody versioned fails CI, and at load it changes the fingerprint and the hash of every changed cell it touches.

**Load decision matrix (M2b §9).**

| Condition | Action |
|---|---|
| `save_format` unsupported | Refuse |
| Checksums fail in the manifest or player | Refuse; offer backups (§7.2) |
| `schema_version` older, chain registered | Migrate step by step |
| `schema_version` older without a chain, or newer | Refuse |
| `content_hash` equal | No definition-ID pass |
| `content_hash` differs | Definition-ID pass: rename, convert, destroy; unresolved refuses naming the ID |
| `worldgen_version` or `rng_contract_version` differs | A registered worldgen migration (the schema chain's 1 -> 2 step is the only one), else refuse |
| Cell `baseline_hash` equal | Apply the delta |
| Cell `baseline_hash` differs, transition registered | Rebase conservatively, validate |
| Cell `baseline_hash` differs, no transition | Refuse |

---

## 7. Atomic write, integrity, and recovery

### 7.1 Write sequence (follow exactly)

```
1. Serialize every section into memory
2. Write to a staging directory beside the slot:  <profile>/.staging-<slot>-<ulid>/
3. Flush each staged file to disk (write-through, then flush)
4. Compute sections.sha256 over the staged tree, from the bytes on disk
5. Commit:
   5a. Rename the current <slot> to .trash-<slot>-<ulid>   (never delete first)
   5b. Rename .staging-<slot>-<ulid> to <slot>
6. Verify: re-read the committed <slot>, re-hash, compare against sections.sha256
7. Only after 6 succeeds: retire .trash-<slot>-<ulid> - into the backup chain if a clean
   load proved it (§7.3), into pre_migration_<schema>_<slot> if a schema migration
   displaced it, otherwise delete it
```

The staging and trash directories sit **beside** the slot and carry its name. An earlier revision put staging inside `<slot>/`, which step 5b could not then rename to `<slot>`. The names let the boot sweep tell which slot a leftover belongs to. **Boot sweep:** with no `<slot>`, a complete (verifiable) staging directory is promoted, or else the newest trash is restored. With a `<slot>` and a trash, a verifiable slot completes the commit and an unverifiable one rolls back. Leftover staging is discarded. Every step boundary is kill-tested on Windows: M2 ME-4 for saves, and the M2b migration kill test for migrations.

**Directory fsync.** .NET on Windows cannot fsync a directory. Step 3 is file-level (write-through and flush), and the step 6 verification plus the boot sweep cover a rename lost to a crash.

**Commit is a retry loop, not a bare rename.** On Windows there is no POSIX rename-over-directory semantics, and the realistic failures are *not* power loss:

- **Sharing violations.** `Directory.Move`/`File.Replace` throws `IOException` when any handle is open anywhere in the tree. Antivirus real-time scanners and the Windows Search indexer open newly written files routinely.
- **Cloud-sync interference.** OneDrive/Dropbox rename, hold, and hydrate files, and can resurrect a deleted `.trash-*` directory.

Therefore step 5 is specified as: **retry with bounded exponential backoff on `IOException`, classifying "transient lock" (retry) from "policy denial" (surface to the player with the path and the error).** A bare rename that fails at 5b leaves the slot renamed to `.trash-<ulid>` with the staging directory un-promoted, so the boot sweep must be able to recover that state or the player has no slot until it runs.

### 7.2 Integrity and quarantine

| Condition | Behaviour |
|---|---|
| `save_format` mismatch | **Refuse to load.** Never guess a container layout |
| A changed cell's `baseline_hash` mismatch, no registered transition | **Refuse**, naming every mismatched cell (§6.4). Never apply a delta to a different baseline. (The localized full-serialization fallback of `D-05` remains a design option; it is not implemented) |
| Unresolved definition ID | **Refuse**, naming the ID and what referenced it (§6.3) |
| `manifest.json` unreadable | Hard error; the slot cannot be loaded |
| `player.msgpack` corrupt | Fatal for the session; offer the rolling backup |
| `sections.sha256` mismatch on one section | Quarantine that section; load without it if it is `cells`/`entities`/`buildings` |
| Quarantined section is `cells`/`entities`/`buildings` | Load **without** it, set `flags.quarantined_sections`, and state exactly what was lost. **Partial recovery beats none** |
| `journal.jsonl` / `command_log.jsonl` partial tail | Truncate to the last complete line; not corruption |
| Post-load invariant validation fails | Domain validator (no item in two containers, no orphan ULID reference, no quest bound to a missing instance) → quarantine the offending record set |
| Reference target is in a **quarantined** section | **Reported loss**, never re-quarantined. The reference was valid; its target was dropped |

**Two distinct failure paths.** A `save_format` mismatch **refuses** — a container layout cannot be guessed. A corrupt *section* is **quarantined and loaded without** — partial recovery is worth more than nothing. `SYSTEMS.md` S-33's "fall back to a backup slot rather than partially loading" describes the refusal case; both paths exist and the difference is deliberate.

### 7.3 Backup rotation

| Artifact | Copies | Retention |
|---|---|---|
| `<slot>` | 1 | Current good save |
| `.bak-<slot>` | **2 generations** | The two previous verified-good saves |
| `pre_migration_<schema>_<slot>` | 1 per migration event | The original a schema migration displaced, kept once. Until the player confirms the migrated save loads (removal is a UI action, not yet built) |
| `.trash-<slot>-<ulid>` | 0–1 transient | Removed only after §7.1 step 6 verifies |

**Two backup generations, retired on a verified _load_, not a verified write.** With one generation retired at the next successful write, a player who saves three times after a silent problem has three bad saves and one good backup that the next write discards. The previous good save the charter promises must survive more than one commit.

As implemented: a displaced save enters the chain only if a **complete, clean load of its bytes as they are on disk** proved it. That means no quarantine, no rejected record, no reported loss, and no migration needed. The proof is recorded in `rotation.json` as the digest of the save's integrity root. An unproven save is dropped when displaced, so it can never push a proven backup out. `ROADMAP.md` M2's "one rolling backup slot" is superseded by these two generations.

### 7.4 Load sequence (follow exactly — this is the only normative load order)

`ARCHITECTURE.md` §8.2 cites this section rather than restating it. Implemented as a **state machine with gated stages**, so an out-of-order call is a type/state error rather than a silent misbehaviour — which is what makes `I-6` structural instead of conventional.

```
a. Read and validate manifest.json        → save_format must match exactly, else refuse (§7.2)
b. Verify sections.sha256                 → per-section; quarantine failures, do not abort (§7.2)
c. Apply the schema migration chain       → in memory, one version per step; a gap refuses (§6.2)
d. Resolve definition IDs                 → when content_hash differs: rename/convert/destroy; unresolved refuses (§6.3)
e. Check the generator contract           → worldgen_version and rng_contract_version; fingerprint drift is reported (§6.1)
f. Prove each changed cell's baseline     → baseline_hash equal, or a registered transition, else refuse (§6.4)
g. Create the world store and registry     → anchored from the manifest
h. Regenerate the cell baseline            → from the baseline tuple, per cell (§1.3)
i. Apply the cell delta                    → per-cell overrides against that baseline (§5.2)
j. Apply the entity delta                  → MERGE ON slot_key, replacing baseline slots; place created instances (§5.3)
k. Recompute derived caches                → ONLY here, after c–j. Never incrementally (§1.1 I-6)
l. Deserialise player and companion state
m. Run invariant validation                → aware of quarantined_sections (§7.2); a record whose baseline_hash does not match is rejected here too
n. Emit ONE WorldLoaded event               → views rebuild exactly once
```

This order is M2b's authority order (`M2B_SAVE_MIGRATION_AND_BASELINE_COMPATIBILITY.md` §16). As implemented, one pipeline serves the load, the dry run and the migration: the stages are private and run in this fixed order. Steps k and n arrive with the first derived caches and the Application wiring. Step l's player decode runs before d, because the definition-ID pass rewrites inventory references.

**Why each ordering constraint exists:**

- **c before d.** The pass needs the current shape to find every stored ID. Migrations therefore treat IDs as opaque strings. (An earlier revision ran aliases first, so that migrations could read resolved IDs. That required each migration to know every historical shape's ID locations, and M2b chose the opposite.)
- **d before f and m.** Proof and validation must see resolved IDs, or a renamed slot family would look like a baseline change, and renamed data would quarantine falsely.
- **f before h-j.** A delta is applied only after its baseline is proven, so no path can apply it to the wrong baseline.
- **h before i.** A delta is meaningful only against the baseline it is a delta *of*. Applying deltas first is the inversion `ARCHITECTURE.md` §8 originally contained.
- **j is a merge, not an append** (§5.3), or killed entities resurrect.
- **k last among the data steps.** A cache computed from a half-migrated world is wrong in a way no later step repairs, and it is invisible because the number is plausible.
- **n exactly once.** Views that rebuild more than once produce visible flicker and double-subscription bugs.

`T-27` asserts this **order**, not the individual steps. Time not played is folded in via `world_time_advanced_ticks` (§5.5) between j and k.

**Dry run.** `save:migrate --dry-run <save>` runs steps a-j into a throwaway registry and prints the report. It covers the source and current save format, schema, content version and hash, and worldgen version and fingerprint, plus aliases, removals, matching and mismatching changed cells, migration steps, warnings, blockers and expected loss. It writes nothing, not even the load proof a real load records.

---

## 8. Autosaves, manual saves, and slots

### 8.1 Scoped saves

A **scoped save** writes only the sections that changed, into a new slot directory, and is triggered by:

- A dirty exterior state at an interior boundary (`WORLD_ARCHITECTURE.md` §8)
- Demotion-triggered state capture (a correctness rule, not an optimization)

Scoped saves are **unconditional when the trigger fires** — a streamer may not discard dirty state — but they are cadence-capped (§8.2) and they are **budgeted separately** from the frame budget, because a correctness rule and a performance budget that collide need an arbiter (`RK-P13`).

### 8.2 Cadence and cost

| Setting | Value | Note |
|---|---|---|
| Autosave interval | 5 minutes of playtime, and on major transitions | Interval is independent of real time |
| Autosave main-thread cost | **≤ 2 ms P99** | `WORLD_ARCHITECTURE.md` §12 budget line |
| Scoped-save cadence cap | At most one per 30 s | Prevents boundary thrash from writing continuously |
| Manual saves | Unthrottled | Player intent wins |
| Max slots | 1 quick + unlimited manual + 5 rolling auto | Rotation is a policy, not architectural: rotating quick slots can be added without a format change. An earlier revision said 10 quick, which contradicted §3.2's single `quick` slot; M2 implemented one (`M2_STATUS.md`) |

The main-thread budget is met by serializing off-thread and committing on-thread; commit is the only main-thread work.

---

## 9. Save size, and what to do when it grows

**Budgets, not measurements.** Nothing has been profiled.

| Section | Budget | Grows with |
|---|---|---|
| `manifest.json` | < 4 KB | Fixed |
| `player.msgpack` | 20–200 KB | Inventory, quest count, journal |
| `companions.msgpack` | 10–100 KB | Roster size (max 6 active) |
| `entities.msgpack` | 2–40 MB | **Diverged instances** (loot, corpses, NPC changes) |
| `cells.msgpack` | 1–20 MB | Visited/diverged cells |
| `buildings.msgpack` | 10 KB–5 MB | Player structures |
| `journal.jsonl` | 100 KB–5 MB | Session length |
| `command_log.jsonl` | 10 KB–2 MB | Commands issued |

**Response order when a budget is exceeded** — in this order, and each step must be measured before the next is taken:

1. **Trim transient entity lifetime.** Corpses and dropped loot decay out of the delta (§5.6's rebase).
2. **Rebase aggressively.** More frequent cell unloads shrink the derivable record set.
3. **Shard by region.** Split `entities`/`cells` per region so an untouched region costs zero bytes.
4. **Archival compression.** Compress cold sections with a per-section codec, recorded in the manifest.
5. **Only then: change the cell size.** This is a tuning constant with a documented migration path (`RK-A6`), and it invalidates every cell key, so it is the last resort.

**Cell-size changes are a migration, not a constant edit.** A change from 100 m to 150 m re-keys every cell, which means every stored `cell_key` and every `slot_key` is stale. The migration must re-key, and the generator must be able to place old overrides into new cells.

---

## 10. Required tests

Every test is headless, engine-free, and a domain-layer concern. Gate column: `P1` = required for the Phase-1 prototype; `P2` = required for the vertical slice.

| # | Test | Assertion sketch | Gate |
|---|---|---|---|
| **T-01** | Save/load round-trip | Full authoritative-state equality after load; no field silently defaulted | P1 |
| **T-02** | Deterministic generation | Same baseline tuple (§1.3) yields a byte-identical baseline across processes and 100 runs, under a pinned numeric and comparison policy; a runtime-only content change leaves it byte-identical | P1 |
| **T-03** | Delta minimality and rebase | (a) Saving twice with no mutation yields an identical `cells`/`entities` section. (b) **After mutate-then-revert, the section shrinks back to byte-identical** — a record that has returned to baseline is rebased away (§5.6) | P1 |
| **T-04** | Inventory transfer | Move stacks between player/container/companion; no duplication nor loss; capacity respected; ULIDs preserved | P1 |
| **T-05** | Equipment persistence | Equip/unequip updates character, slots, derived stats; durability and charges survive | P1 |
| **T-06** | Death persistence | Death state (debt, corpse location, injury) persists and resolves exactly once across save/load | P1 |
| **T-07** | XP persistence | Award paths, caps and partial progress survive reload | P1 |
| **T-08** | Leveling persistence | Level-up effects, unspent points, and reputation survive reload | P1 |
| **T-09** | Crafting persistence | Recipe execution, quality roll, input consumption, station state persist; RNG **not** re-rolled on reload | P1 |
| **T-10** | Resource consumption | Consumables, charges, and fuel decrement exactly once; consumed entities survive | P1 |
| **T-11** | Quest state persistence | Objectives, branches, timers, hidden objectives, failures persist; nothing lost across load | P1 |
| **T-12** | Companion state persistence | Affinity, orders, injury, and personal-quest flags survive; no duplicate companions on reload | P1 |
| **T-13** | Corruption recovery | Truncated / corrupted / checksum-mismatched fixtures follow §7.2 exactly; never silent success | P1 |
| **T-14** | Atomic write crash injection | Kill between every step of §7.1; every resulting slot is either old-complete or new-complete | P1 |
| **T-15** | Content mismatch handling | A content-pack change with a matching `schema_version` runs the alias pass and no chain; an unresolved ID is a hard error naming the ID | P1 |
| **T-16** | Migration chain | A fixture at every supported `schema_version` migrates step-by-step to current and passes T-01; a cached value is never migrated (§6.2) | P1 |
| **T-17** | Baseline regeneration integrity | Unmodified cells and unmodified NPCs regenerate identically; a changed cell whose `baseline_hash` differs **refuses** rather than applying a delta, unless a registered transition rebases it (§6.4) | P1 |
| **T-18** | Entity identity and slot merge | A persisted entity replaces its baseline slot (§5.3): a killed generic NPC does **not** resurrect, and no slot holds two live occupants | P1 |
| **T-19** | Loot generation | Generated loot is reproducible from the keyed derivation `hash(world_seed, purpose_key)`; reload cannot reroll a chest; no global counter is read | P1 |
| **T-20** | Relationship changes | Relationship deltas persist and remain attributable to their source events | P2 |
| **T-21** | Faction reputation | Reputation values plus their bounded event log persist; no oscillation across reload | P2 |
| **T-22** | Spawn/respawn and catch-up purity | A node's ready time is identical across save/load **and one 300-day step equals 300 one-day steps** (§5.5); chunk size is unobservable | P1 |
| **T-23** | Content alias/tombstone pass | Renames resolve; removals convert/destroy/quarantine per §6.3; unresolved IDs are a hard error naming the ID | P2 |
| **T-24** | Save-size budget | A golden long-play fixture stays inside the §9 budget; regression fails CI | P2 |
| **T-25** | Autosave hitch | Main-thread save cost ≤ 2 ms P99 on the target machine under a loaded world | P2 |
| **T-26** | Tier-transition legality | (Co-owned with `WORLD_ARCHITECTURE.md` §7) promotion never adopts illegal abstract state; save/load at a tier boundary is consistent | P2 |
| **T-27** | **Load-order sequence** | **Asserts §7.4's order rather than its steps.** One fixture save carrying (a) an old `schema_version` needing migration, (b) a renamed ID resolvable only via the alias map, (c) a persisted derived value that must be recomputed, and (d) a deliberately corrupt quarantinable section. Assert: `save_format` mismatch **refuses** while a corrupt `cells`/`entities` section **loads** with `flags.quarantined_sections`; every derived cache reflects **migrated** inputs, never pre-migration ones; no dangling reference survives a resolvable alias; and exactly **one** `WorldLoaded` event is emitted | **P1** |
| **T-28** | **Command-log replay** | Feed a save's `command_log.jsonl` into a fresh world and assert **digest equality** with the original final state. This is what makes the replay tuple (§1.3) true rather than aspirational | **P1** |

---

## 11. Risks, open problems, assumptions

### 11.1 Document-local risks

Local IDs are retained for detail. Risks promoted to the project register are cross-referenced there.

| ID | Risk | Status |
|---|---|---|
| **RK-P01** | Generation-code drift without a version bump silently corrupts every save | **Mitigated** (M2b): per-cell `baseline_hash` verified at load (§6.4), `worldgen_fingerprint` with canonical probe digests pinned in CI, and the world builder's last-line rejection of any record whose baseline does not match; promoted as `RK-01` |
| **RK-P02** | Dirty-flag incompleteness loses world changes while the save looks valid | **Reduced**: the flag is a hint, the authoritative set is derived at save time (§5.2) |
| **RK-P03** | Definition-ID renames break live saves | Mitigated by the alias pass (§6.3); promoted as `RK-03` |
| **RK-P04** | Building volume exceeds the save budget | Promoted as `RK-06` |
| **RK-P05** | Offline catch-up semantics: anything not derived from `(state, tick)` breaks | **Mitigated**: honoured advance is persisted, chunking unobservable (§5.5) |
| **RK-P06** | Windows atomic-rename semantics; AV/indexer sharing violations; sync-folder interference | **Reduced.** The §7.1 sequence with retry-and-classify is implemented and kill-tested at every step on Windows (M2, M2b). A save root inside OneDrive or Dropbox is detected so the game can warn. Still open: real sync-engine interference is untested; promoted as `RK-13` |
| **RK-P07** | Migration-chain testability | Bounded by the fixture-per-version requirement (§6.2, T-16) |
| **RK-P08** | Quarantine UX: what the player is told was lost | **Open, and not merely presentational.** §7.2's reported-loss rule is normative; the player-facing statement is Phase 1 UI work |
| **RK-P09** | **Delta growth has no bound without rebase** | **Addressed** by §5.6 (`I-7`); retained here because the bound is now a mechanism rather than a hope |
| **RK-P10** | **Entity-to-baseline identity collision (spawn vs. delta)** | **Addressed** by the slot key and merge-on-load rule (§5.3, `I-8`) |
| **RK-P11** | **RNG draw-order sensitivity** | **Addressed** by keyed derivation and removing the global counter (§1.1 `I-9`), and for generation by RNG contract 2's addressed semantic channels: an added draw moves nothing that exists (M2b) |
| **RK-P12** | **Content tuning silently reinterprets persisted derived values** | **Addressed** by the shape/tuning split and baseline-locking (§5.5, §6.1) |
| **RK-P13** | **Scoped saves are a mandatory, unbudgeted side effect** | **Open.** §8.1 caps cadence and budgets separately; the arbiter between the correctness rule and the frame budget is stated but not measured |
| **RK-P14** | **Content identity used as procedural entropy**: M2 seeded every cell stream with the whole `content_hash`, so any content edit moved the entire world | **Addressed** (M2b): content identity is not a generation input (§1.3); per-cell baseline proof; content change classes (§6.1); a baseline-neutral edit is tested to load with no reshuffle |

### 11.2 Assumptions

1. The save system runs on Windows desktop; §7.1's commit sequence is kill-tested at every step on Windows (M2, M2b).
2. All tests are headless; nothing has been profiled in-engine.
3. All numbers in §9 and §8.2 are **budgets** rather than measurements; nothing has been proven yet.
4. `worldgen_fingerprint` does not hash the generation assembly's bytes, which are not reproducible across builds. It covers the generator's declared identity, its contract versions, its placement data, and its **output** on the canonical probe cells, so a code change is detected by what it does. A change that alters no probe cell's output is still caught at load, by the hash of any changed cell it touches.

---

Action: owner review and sign-off on this file before moving to Phase 1 design.
