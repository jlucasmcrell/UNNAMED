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
| Navigation grid (`D-13`, M7 reconciliation (2026-09-24)) | Derived in the domain: rebuilt in the `Simulation` constructor from the region layout and the placed pieces, and restamped on each footprint change; never saved. A mover's committed route is mover state and **is** saved with the body it moves |
| Window stamps outside a route; the navigation scratch and counters (M7) | Recomputed by the follower, or allocated on first use; a stamp is saved only inside a route, as its cache key |
| Structure-derived space: `SystemContext.Space`, the closed piece-leaf cache, the socket index, `StructureFootprints`, and the structure and errand audits (M7) | Rebuilt by the building and NPC systems' `Populate` from the piece and errand rows plus their definitions, in `StructureOrder`; again on each place, dismantle or destroy (the leaf cache alone on a door toggle) |
| A piece's `health_max`, footprint, bounds, sockets, chest site and station anchor; an errand's goal (M7) | Read from the piece's definition, or the NPC's authored site, at use; a route records the goal it was planned for, and a moved goal replans |
| `Simulation.StructureRevision` (M7) | Equal to the persisted `structure_seq` |
| Faction tier and level, relevance, membership, faction views (M7) | Derived from content and the saved points on every read; tiers are never stored, so a ladder retune is not save-locked |
| Registry entries for placed pieces (`pce_`) and their chests' containers (M7) | Registered by `WorldDelta.FromSnapshot` as each row applies; the registry is never saved |
| Collision, occlusion, LOD meshes, GPU resources | Baking / streaming pipeline |
| Pathfinding search state, AI blackboards, animation state, ragdoll, physics contacts | Fresh instantiation on tier promotion (`WORLD_ARCHITECTURE.md` §7.3) |
| Presentation-only state (camera, HUD layout, particles, subtitles) | Presentation defaults + optional `client_prefs.json`, not part of the save (`D-11`) |
| Derived caches (encumbrance, faction power, settlement wealth) | Recomputed **once**, after every migration and alias resolution (§7.4 step k); if cached on disk, marked `derived: true` and discarded on any doubt |
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

**As built (M7, schema 16).** Four files hold the save: `manifest.json`, `player.msgpack`, `entities.msgpack` and `cells.msgpack`, with `sections.sha256` over them. `companions.msgpack`, `buildings.msgpack`, `journal.jsonl`, `command_log.jsonl` and `orphans.msgpack` are not built. M7's lists live inside the existing files: the faction ledger and each companion's route in `player`, and placed pieces, the structure sequence and NPC errands in `entities`. A new section file would need a schema-aware integrity root first, because the root lists a fixed set of files.

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
  "schema_version": 8,                    // GAMEPLAY STATE schema; drives the migration chain
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

These are the only fully-serialized sections. A character is not regenerable, so nothing here is a delta. Includes: identity (ULID, name, species ref, appearance seed, archetype), the progression record (from schema 4: level and progress, XP debt, attribute allocation, one-time grants and unspent attribute points, skill levels and progress, the technique/formula knowledge record with learning sources, current Health/Stamina/Focus/Strain, the anti-farm guard state — `PROGRESSION.md` §3–§4), equipment slots and per-instance state, inventory contents by ULID, quest instances, faction reputation and crime records, relationship values and the memory log, discovered-location records, and lifetime XP totals per source kind. The detailed per-axis advancement telemetry is a separate, non-shipped log (`PROGRESSION.md` §13.2), never part of a save.

**Rebase does not apply here.** There is no baseline to return to, so every field is authoritative and T-01 asserts full equality after reload.

**Relationships and conversation memory (schema 10).** The player section holds what each NPC thinks of the player - `npc_id`, `dimension`, `value`, non-zero values only - and, per conversation, the lines heard (`dialogue_id`, `heard`: node IDs), which keep a one-time line spent across a load. Both go through the definition-ID pass. A trader's wares are not a new record: they are a changed container (schema 6) keyed by the merchant profile, proven against the trader's cell.

**Quests (schema 11).** The player section holds every quest started: `quest_id`, `status` (`active`, `completed`, `failed`), `started_tick`, and once it has ended `ended_tick` and `ended_by` (the objective that completed or failed it, or `fail_if[n]`), with its `objectives` - `id`, `status` (`active`, `satisfied`, `failed`, `closed`), `activated_tick`, `ended_tick` once over, and `progress`, the count of deeds done while it was active, which is not derivable from the world. Quest IDs go through the definition-ID pass (two records that resolve to one quest keep the one that went further, then the earlier start); objective IDs are the quest's own. The debugger's trace is not saved.

**Companions (schema 12).** The player section holds the companions the character has recruited: `npc_id`, `order` (`follow`, `wait`), `condition` (`up`, `downed`), where they stand (`x_mm`, `z_mm`, `facing_mdeg`; the height is the terrain's), `health`, and what their next ticks depend on - `downed_tick`, `stuck_ticks`, `last_combat_tick` and `trail_mm`, the marks of the character's trail they are walking - so a loaded world goes on as the saved one would. A blow in progress is not kept, as with creatures. NPC IDs go through the definition-ID pass; a companion whose NPC was removed is dropped. **Reconciliation:** §5.1's heading names a separate `companions.msgpack`; Phase 1's roster is the character's and holds no equipment or progression of its own, so it lives in `player.msgpack` - the separate section arrives when companions carry state of their own.

**Posture (schema 13, the owner's M6 playtest).** The player section holds the body's `posture`: `stance` (`standing`, `crouched`), `airborne`, and `air_ms`, how long it has been in the air - so a save made mid-jump lands where the saved world would have, and one made crouched under a beam loads crouched. Older saves stand on the ground; a schema-13 player without it is corrupt, not defaulted.

**Factions (schema 15, M7).** The player section holds the faction ledger (`factions`): `next_act_seq`; the `acts` the player's deeds made - `seq`, `kind` (`creature_killed`, `switch_set`), `subject` (a definition ID), `cell_key`, `x_mm`, `z_mm`, `tick` - strictly ascending and bounded by the act-log capacity; the `knowledge` rows - `knower` (a faction), `act`, `identity` (`unidentified`, `identified`), `source` (`witnessed`, `reported`), `via` (the NPC who reported it, or none), `tick` and `delta` - sorted by knower then act; and `standing`, each faction's non-zero points in [-1000, 1000]. Tiers and levels are derived, never stored. Faction, NPC and subject IDs go through the definition-ID pass: a removed subject takes its act and what was known of it; merged factions keep one knowledge row an act and sum their standing, clamped, and a merge to 0 drops the row with a warning; a `via` that no longer resolves blocks the load. A schema-15 player without a valid ledger is corrupt, not defaulted.

**Routes (schema 15, M7).** Each companion carries its committed route (`route`): `status` (`none`, `active`, `unreachable`), `goal_mm`, `corners_mm` (at most 32 corners), `planned_tick`, `stamp` (the navigation window's stamp it was planned against), `watch_mm` (the rectangle whose change invalidates it) and `partial`. A route is mover state: its next ticks depend on it and nothing reproduces it, so a loaded companion walks on as the saved one would. A companion without a valid route is corrupt, not defaulted.

**Vitals (schema 16; the owner's ruling on the M7 E8.5 STOP).** The player section holds what the character's pools do next (`vitals`): `last_combat_tick`, `last_exertion_tick` and `last_cast_tick`, the absolute world ticks of the last blow taken or dealt, the last exertion and the last working, each of which begins the pause before a pool returns (health's `out_of_combat_s`; stamina's regeneration delay; Focus's and Strain's delays), or -1000000 for never; and `health_milli`, `stamina_milli`, `focus_milli`, `strain_milli` and `sprint_milli`, the thousandths each pool has accrued towards its next whole point and a sprint has drained, each 0-999. So a save taken in a fight, after a sprint or after a working goes on as the unsaved world would: health returns on the same tick, not early. A value no running world holds (a part-point outside 0-999, a tick below 0 other than -1000000) is corrupt, and a schema-16 player without vitals is corrupt, not defaulted.

**What the character's combat state does not keep (the contract).** A blow or an action in progress is not saved, for the character as for creatures and companions: a swing, dodge, working or stagger under way, a raised guard, the stagger immunity after a stagger, and the last blows kept for a death recap. A load resumes at rest in all of them. A save taken while one is under way can therefore go on differently from the unsaved world - a creature's bite one tick from landing is started again after the load - and the equality of a save and its continuation holds only for saves taken outside them.

**As built (schema 16).** The player section holds: the character's ULID and name; position in integer millimetres; facing in millidegrees (schema 5); `appearance_seed` (required from schema 3); inventory stacks by item ULID, each with its quality (schema 9); equipment slots naming carried items, and the purse (schema 6); active status effects (schema 7); the progression record (schema 4; `PROGRESSION.md` §3-§4); discovered-location records (schema 5); relationships and conversation memory (schema 10); quests (schema 11); companions, each with its route from schema 15 (schema 12); the posture (schema 13); the faction ledger (schema 15); and the vitals (schema 16). The record's enums are saved as snake_case keys, never ordinals; its pool maxima and attribute totals are derived at run time, never stored; and every definition ID in it goes through the definition-ID pass. Crime records, lifetime XP totals per source kind and a species reference are not built.

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

**Created instances (schema 3).** A persistent instance that no baseline slot generates - a dropped item, a placed chest - is stored whole in the section's `created` list: `instance_id`, `def_id`, `host_cell`, position, (from schema 6) `count`, and (from schema 9) `quality`. It is proven against its host cell's baseline like a slot record.

**Changed world containers (schema 6).** An authored container's contents are its loot table's result, rolled from its semantic key, until the player first changes them. From then on the section's `containers` list holds its `key`, its `instance_id`, its `host_cell`, and its whole contents (`item_id`, `def_id`, `count`, and from schema 9 `quality`), proven against the host cell's baseline like a created instance. Its item definition IDs go through the definition-ID pass.

**Item quality (schema 9).** Every stack - carried, in a changed container, or lying in the world - records its `quality`: -1 crude, 0 standard, 1 fine (M3f). It is the instance's, never the definition's, and a stack holds one quality: two stacks of one definition at different qualities never merge. A missing or out-of-range quality in a schema-9 save is corruption, never defaulted.

**Creature records (schema 8).** A spawner's creature that differs from its baseline - moved, wounded, dead, gone, or holding a mind other than rest - is stored in the section's `creatures` list, keyed by its derived `spawner#member` key and carrying its generation (the `generation_seq` above). It records the `instance_id`, `def_id`, `host_cell` and the host cell's `baseline_hash`, the condition (`alive`, `corpse`, `gone`), position, facing, health, the tick it died and the absolute tick it is due back, and its mind: awareness, whether and where it knows its target to be, when it last saw it, its search deadline, and whether it has called. A record at baseline is not stored (§5.6); load merges on the key, so a dead creature stays dead. A corpse's contents are an ordinary changed container once the player has touched them. Definition IDs go through the definition-ID pass. Idle wander and patrol derive from the world tick, so they need no storage; an attack in its windup is transient, like the player's.

**Creature continuation and pending sounds (schema 14; the Phase-1 technical audit, L-06).** Each creature record also carries what its next ticks depend on (`continuation`): `next_charge_tick`, the first tick it may charge again; `stagger_immune_until`, the first tick a blow may stagger it again; and the stagger it is in - a charger's stun among them - as `staggered_tick` and `stagger_lasts_ticks` (0 for the usual length). Each is written only while it still matters, so a creature over its cooldown, immunity or stun has the record of one that never had them. The section's `noises` list holds the sounds made on the last tick that creatures hear on the next - a blow, and a howl with its caller's kind (`caller_kind`, through the definition-ID pass) - in the order they were made. A save therefore goes on as the unsaved world would: a stunned boar stays down, a charger waits out its cooldown, and a howl made on the tick of the save still brings the pack. A blow in progress - an attack's windup, a charge's run - is still not kept.

**Placed pieces, the structure sequence and NPC errands (schema 15, M7).** The section's `pieces` list holds every player-placed building piece, sorted by ID: `instance_id` (a `pce_` ID derived from the owner and the piece's ordinal, `EntityId.Derived(Piece, n, "unnamed.piece/v1", owner)`), `def_id`, `host_cell` (the cell of its anchor), the anchor in absolute millimetres (`x_mm`, `z_mm`), `rotation` (a quarter turn, 0-3), `owner`, `health` and, for a door, `door_open`. Its shape is its definition's; only what the definition cannot know is stored. `structure_seq` is the last ordinal minted, so a dismantled piece's ordinal is never reused. The `npc_errands` list holds each named NPC away from their authored site, sorted by NPC ID: `npc_id`, `host_cell`, `phase` (`to_work`, `at_work`, `to_home`), the `piece_id` and `work_owner` of the work place, the pose (`x_mm`, `z_mm`, `facing_mdeg`), `stuck_ticks` and the committed `route`. Pieces and errands are proven against their host cells' `baseline_hash` like created instances. Rows are checked one by one on load, and an invalid row is rejected alone and reported. A piece chest is an ordinary changed container (schema 6) whose key is `container.` and the piece's ID in lower case, and whose ID is derived from the piece's (`Derived(Container, n, "unnamed.piece-container/v1", piece)`). A chest whose piece is absent, or whose ID is not its derived one, is rejected. A removed piece definition spills its chest's items on the ground, each keeping its ID. A schema-15 section without `pieces`, `structure_seq` or `npc_errands` is corrupt, not defaulted.

### 5.4 `buildings.msgpack` — player structures

A building is a ULID-keyed structure with a footprint of one or more cells; pieces are ULID-keyed rows with a `def_id` and a socket path (`WORLD_ARCHITECTURE.md` §10). Nothing here is regenerable: a player structure exists nowhere else, so corruption is unrecoverable rather than merely annoying, and the quarantine path must name every lost structure.

`storage` entries reference container ULIDs that live in `entities.msgpack`. That cross-section reference is exactly the case §7.2 must handle when `entities` is quarantined: a dangling reference whose target section was dropped is **reported loss**, not a corruption, and must not be re-quarantined as if the reference itself were invalid.

**As built (M7).** There is no structure record and no `buildings.msgpack`. Each placed piece is a `pce_` row in `entities.msgpack` (§5.3); a dismantle or its destruction retires the row and its identity. A piece chest's contents are a container in the same section, so no cross-section reference exists. A quarantined `entities` section loses the pieces, the structure sequence, the errands and the chests together, and the warning names the section.

### 5.5 Node, spawn, and time-derived state

**Node keys are derived, not stored ULIDs**, because a resource node is a property of the world rather than an instance the player owns: `node.<cellkey>.<rule>.<ordinal>`, where `<rule>` is the placement rule's semantic name and `<ordinal>` is the node's two-digit ordinal within that rule (schema 2+). That is what lets a pristine cell cost zero bytes. M2's keys were `node.<cellkey>.<index>`, a position in the generator's output list, so adding a rule re-keyed every later node; the 1 -> 2 migration maps them (§6.2).

Stored state per harvested node: `state`, `last_harvest_tick`, `harvest_seq`. The ready time is:

```
ready_tick = last_harvest_tick + respawn_window(node_def) ± jitter(hash(node_key, harvest_seq, world_seed))
```

Time not played still advances world time (abstract catch-up), so a node harvested before a session break is correctly regrown on return.

**As implemented (M3f).** A node's record is its `last_harvest_tick` and its `harvest_seq` (how many harvests it has had), in its cell's delta; there is no separate `state`. Readiness is derived, never stored: a finite node is ready while `harvest_seq` is below its charges and never refills; a daily node is ready once a world-day boundary has passed since its last harvest (no jitter in Phase 1). Phase 1's nodes are authored in the region (`DATA_MODEL.md` §4.16) and are part of their cells' baselines, so their keys are `node.<cellkey>.<name>.00`. Adding them changed the baseline of the two cells that hold them; the session registers the transition from the baseline without them (`M3f: the region's resource nodes`), so an older save carries onto the new baseline like any other: nothing it holds was a node.

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
| 3 -> 4 | The player gains the progression record (M2c). A save that predates progression gets its empty value: level 1, no XP or debt, nothing allocated, learned or practised, full pools, no guard history. The step needs no content |
| 4 -> 5 | The player gains facing and discovered-location records (M3). An older save faces +Z (0) and has discovered nothing, since nothing could be discovered before M3 |
| 5 -> 6 | The player gains equipment slots and a purse; a created instance gains its count; the entities section gains changed containers (M3b). An older save had nothing equipped, no coin, single created items and untouched containers |
| 6 -> 7 | The player gains active status effects (M3c). An older save had none, since nothing could apply one before M3c |
| 7 -> 8 | The entities section gains creature records (M3d). An older save has none: every creature stands at its spawner's baseline, since no creature state was saved before M3d |
| 8 -> 9 | Every item stack gains its quality: carried, in a changed container, or created in the world (M3f). An older save's stacks are all standard, since nothing could make another quality before M3f |
| 9 -> 10 | The player gains relationships and conversation memory (M4). An older save has neither: there was no one to talk to before M4 |
| 10 -> 11 | The player gains quests (M5). An older save has none: there were no quests before M5 |
| 11 -> 12 | The player gains companions (M6). An older save has none: no one could join the character before M6 |
| 12 -> 13 | The player gains a posture (the owner's M6 playtest). An older save stands on the ground |
| 13 -> 14 | Every creature record gains its continuation, and the entities section its pending sounds (the Phase-1 technical audit, L-06). An older save kept none of it: its creatures resume free of any cooldown, stagger or immunity, and nothing waits to be heard |
| 14 -> 15 | The player gains the faction ledger and each companion a route; the entities section gains placed pieces, the structure sequence and NPC errands (M7). An older save knows no act, has nothing built and no errand, and its companions have no route. Nothing is reconstructed from the world: a wolf's corpse or a set lever is no act |
| 15 -> 16 | The player gains the vitals (the owner's ruling on the M7 E8.5 STOP). An older save kept none of them, so its character resumes at rest - every pause over, nothing accrued - exactly as every older save has always loaded. The timing an older save did not keep cannot be reconstructed: a schema-15 save taken in a fight still regenerates early after its load |

**Historical fixtures (M2b §11).** Every schema version that has shipped has a committed fixture written by that version's own writer (`tests/Persistence.Tests/Fixtures/`, policy in its README). CI loads every fixture under the current code, and migrates every one through the commit path, to its committed expected current state. A schema bump without a fixture, a chain step, or an updated expectation fails CI.

```csharp
// Values derived from 'might' are NOT recomputed here: they are caches, and the domain layer
// recomputes them after load. Never migrate a cache — that is how stale numbers travel.
```

**Scenario A — split a derived stat.** `schema_version 14 → 15`. Players have one `might` stat; design splits it into `strength` (melee power) and `endurance` (carry, stamina). The migration writes both new fields. Values *derived* from `might` are caches and are recomputed after load (§7.4 step k), not migrated.

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

**M7 adds no transition.** M7's content changes add no generator input (a container is layout), so its fingerprint is M6's. A renewable node, when one arrives, brings its own transition and a frozen fingerprint.

**The game's registered transitions.** M3f: saves from before the region placed its resource nodes carry every record onto the baseline that has them (nothing they hold was a node). M6: saves from M3's layout (M3f to M5) carry onto the content bible's four cells; the iron seam moved from the north shelf to Blackvein Cut, so a harvest record against the old seam is declared lost, and containers, creatures and created instances carry as they are. The M3 layout's fingerprint is a frozen constant in `GameSession`.

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
   displaced it, otherwise into .prev-<slot> (a quick or manual slot; §7.3) or delete it
```

The staging and trash directories sit **beside** the slot and carry its name. An earlier revision put staging inside `<slot>/`, which step 5b could not then rename to `<slot>`. The names let the boot sweep tell which slot a leftover belongs to. **Boot sweep:** with no `<slot>`, a complete (verifiable) staging directory is promoted, or else the newest trash is restored. With a `<slot>` and a trash, a verifiable slot completes the commit and an unverifiable one rolls back. Leftover staging is discarded. Every step boundary is kill-tested on Windows: M2 ME-4 for saves, and the M2b migration kill test for migrations.

**Directory fsync.** .NET on Windows cannot fsync a directory. Step 3 is file-level (write-through and flush), and the step 6 verification plus the boot sweep cover a rename lost to a crash.

**Commit is a retry loop, not a bare rename.** On Windows there is no POSIX rename-over-directory semantics, and the realistic failures are *not* power loss:

- **Sharing violations.** `Directory.Move`/`File.Replace` throws `IOException` when any handle is open anywhere in the tree. Antivirus real-time scanners and the Windows Search indexer open newly written files routinely.
- **Cloud-sync interference.** OneDrive/Dropbox rename, hold, and hydrate files, and can resurrect a deleted `.trash-*` directory.

Therefore step 5 is specified as: **retry with bounded exponential backoff on `IOException`, classifying "transient lock" (retry) from "policy denial" (surface to the player with the path and the error).** A bare rename that fails at 5b leaves the slot renamed to `.trash-<ulid>` with the staging directory un-promoted, so the boot sweep must be able to recover that state or the player has no slot until it runs.

**Failures the player can be told about (Phase-1 audit remediation, 2026-09-24, M-02, L-03, M-07).**
- **A failed write step.** An IO failure in any of steps 2-5 (a full disk, a folder the player may not write to, a file held open elsewhere) is undone at once: the staging directory is removed, and at 5b the displaced save is moved back. The failure surfaces as a `SaveException` saying the previous save is untouched. The boot sweep finishes anything the undo could not.
- **A failed retirement.** A step-7 retirement that fails leaves the displaced save in its trash directory for the sweep. The new save is already committed and verified, so the save does not fail.
- **The load's proof.** Replacing `rotation.json` retries like a rename. Windows reports a file held without delete sharing as access denied, so that is retried too, briefly. A load whose proof cannot be written still loads, with a warning. A load that cannot read its files is a `SaveException`.
- **A failed autosave.** It never throws out of the frame. It is reported in the frame's result and tried again after 30 s of play, then 60, 120 and 240, up to the interval.
- **One game per profile.** A game holds `<profile>/.lock` open and unshared for as long as it runs, and takes it before its boot sweep. A second copy of the game on the same profile refuses to start instead of sweeping away the first one's commit in flight.

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
| `.prev-<slot>` | 0–1 (quick and manual slots) | The save last displaced before a load proved it; replaced by the next one (B-01, below) |

**Two backup generations, retired on a verified _load_, not a verified write.** With one generation retired at the next successful write, a player who saves three times after a silent problem has three bad saves and one good backup that the next write discards. The previous good save the charter promises must survive more than one commit.

As implemented: a displaced save enters the chain only if a **complete, clean load of its bytes as they are on disk** proved it. That means no quarantine, no rejected record, no reported loss, and no migration needed. The proof is recorded in `rotation.json` as the digest of the save's integrity root. An unproven save never enters the chain, so it can never push a proven backup out. `ROADMAP.md` M2's "one rolling backup slot" is superseded by these two generations.

**The previous save (Phase-1 audit remediation, 2026-09-24, B-01).** A quick or manual save displaced before any load proved it is not deleted. It is kept one deep as `.prev-<slot>`, beside the chain and never in it. Otherwise a relaunch's first quicksave destroyed the last session's quick save outright. The next unproven displacement replaces it. An autosave keeps no previous copy, because its four siblings are its history. `Delete(slot)` removes it with the slot. Like a backup, it loads only when the player chooses it, and loading it proves nothing. The start screen lists every slot newest first, read from its manifest and integrity root without loading it, with each slot's backups and previous copy beneath it. Continue loads the newest slot whose copy is whole and readable. A newer one it passes over is shown with the reason, never skipped silently.

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

**Steps g-j as built (M7).** `WorldDelta.FromSnapshot` sets the structure sequence, then applies cells, slot entities, created instances, pieces, containers, creatures and errands, in that order: pieces before the chests and errands that reference them. Step k's derived state is then rebuilt in the `Simulation` constructor. Navigation is built from the layout and the pieces; the building and NPC systems then rebuild the structure-derived space, the audits and the errand bodies from the rows (§2).
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

**As implemented (Phase-1 audit remediation, 2026-09-24, P-01).** The autosave and the quicksave are taken on the frame and written in the background.
- **What stays on the frame.** The capture: `SaveDocuments.Capture` at the tick boundary, which rebases the delta and returns an immutable document. The manifest is stamped with the capture time, so saves order by the moment they hold.
- **What moves off it.** Encoding, the staging write, the integrity root, the commit, its verification and rotation all run on one background writer, one save after another in the order they were taken, each through the unchanged §7.1 sequence. So the commit is off the main thread too, which is more than this section asked. The main thread holds no lock a commit needs. The writer is a thread of the session's own (`SaveLane`, M7, 2026-09-26), not the shared thread pool, where a save had waited up to 13 s to begin while other work held every pool thread. The thread ends when the session is disposed, after the saves queued on it.
- **A second autosave due while one is being written.** It is not started. The next is counted from the capture of the one being written.
- **A quicksave during an autosave.** It is captured at once and written after the autosave.
- **A failure.** It comes back as an outcome in a later frame, never an exception. A failed autosave is retried 30 s after its capture, then 60, 120 and 240.
- **A load.** It waits for the saves being written first.
- **Quitting.** It waits up to 10 s for a save in flight. The commit is atomic even if the process is killed during it.
- **Synchronous saves.** The harnesses' synchronous `Save` waits for any save taken before it.

---

## 9. Save size, and what to do when it grows

**Budgets, not measurements.** Nothing has been profiled.

| Section | Budget | Grows with |
|---|---|---|
| `manifest.json` | < 4 KB | Fixed |
| `player.msgpack` | 20–200 KB | Inventory, quest count, journal |
| `companions.msgpack` | 10–100 KB | Roster size (player + up to 3 active; the prototype has one) |
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

**Schema 16's tests.** `VitalsContinuationTests` (Application): a save inside health's regeneration pause keeps what is left of it, stamina after swings and a sprint, and Focus and Strain after a working, each compared tick by tick with the unsaved world and then field by field. `Schema16Tests` (Persistence): the vitals round-trip exactly, edges included (never, tick 0, 999); an impossible value is refused; a player without vitals is corrupt; the 15 -> 16 step alone gives a character at rest and changes nothing else, the same bytes every time. `PlayerRecordCompletenessTests` reaches every vitals field, each moving the digest. T-16 over v1-v16.

**Schema 15's tests (M7).** T-01: `T01_M7State_RoundTripsEveryField_ByteStable`, the capture tests (`ACompanionRoute_SurvivesPopulateAndCapture`, `TheFactionLedger_SurvivesStartAndCapture`) and `ASaveAndALoad_CompareEqual_FieldByField`. T-03: the same round trip's byte-identical resave. T-13: the corrupt-not-defaulted tests (`ASchema15…`, `AnErrandWithoutARoute…`), the quarantine test and the row-rejection tests (`AnInvalidPieceRow_IsRejectedAlone`, `AnInvalidErrandRow_IsRejectedAlone`, the two piece-chest tests, `ACellsQuarantine_LeavesPiecesAndErrandsWhole`). T-16: `Fixture_LoadsToItsExpectedCurrentState` and `Fixture_MigratesThroughTheCommitPath_AndReloadsToTheSameState` over v1-v15, and `Schema14To15_GivesNothingBuiltNoLedgerAndNoRoutes`. T-21: the faction ledger's bounded act log, through the same round trip and the ledger's definition-pass tests. T-23: the definition-pass tests over pieces, errands and the ledger (`PiecesAndErrands_GoThroughTheDefinitionPass…`, `TheFactionLedger_GoesThroughTheDefinitionPass…`, `TwoFactionsMergedWithOppositeStanding…`, `AnUnmappedVia_IsABlocker`, `ASpilledChestItemWithARenamedDefinition_LandsRenamed`, `ADefinitionPassThatBreaksARecord_IsABlocker_NotACrash`).

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
