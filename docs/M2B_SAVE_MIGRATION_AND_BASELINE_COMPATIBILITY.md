# M2b — Save Migration & Baseline Compatibility

**Project:** Otherreach  
**Repository codename:** UNNAMED  
**Milestone:** M2b  
**Class:** INFRA  
**Depends on:** M2  
**Status:** Implementation specification  
**Purpose:** Make save evolution safe before progression, combat, quests, and other systems begin adding long-lived schema.

---

## 1. Why M2b Exists

M2 established:

- canonical runtime instance identity;
- deterministic cell generation;
- sparse deltas over a deterministic baseline;
- crash-safe save commits;
- checksummed save folders;
- corruption reporting/recovery;
- backup rotation;
- autosave slots;
- determinism tests.

M2b exists so later code can change without silently making existing saves meaningless.

The rule is:

> **A save must never be applied to a baseline it was not proven compatible with.**

And:

> **A content change is not automatically a world-generation change.**

Those two rules resolve the most important ambiguity exposed at the end of M2.

---

# 2. The M2 Problem That M2b Must Fix

The current M2 implementation reportedly feeds the whole compiled `content_hash` into cell random generation.

That means an unrelated change such as:

```text
creature.wolf.max_health = 30
→
creature.wolf.max_health = 31
```

changes the global content hash and therefore can move every baseline random decision in every cell.

That is too broad.

A wolf-stat balance change must not reshuffle:

- rocks;
- trees;
- foliage;
- resource nodes;
- encounter slots;
- decorative clutter;
- unrelated creature placement.

`content_hash` is useful as exact content identity.

It must **not** serve as global procedural-random entropy.

---

# 3. Compatibility Identities Must Be Separated

The save format should distinguish at least the following concepts.

## 3.1 `save_format`

Describes the physical/container layout of the save itself.

A loader that does not understand the save format must refuse to guess.

Example:

```yaml
save_format: 1
```

---

## 3.2 `schema_version`

Describes the shape/meaning of serialized authoritative data.

Examples of schema changes:

- adding a required field;
- renaming a persisted field;
- changing a field representation;
- splitting one record into two;
- changing an enum representation.

Schema changes use an ordered migration chain:

```text
v1 → v2 → v3 → ...
```

---

## 3.3 `content_version`

Human-readable release/content-pack version.

This is useful for:

- diagnostics;
- patch notes;
- migration reporting;
- support.

It should not be the sole compatibility authority.

A semantic version string is acceptable if the project already uses one.

---

## 3.4 `content_hash`

A cryptographic hash of the exact compiled content pack.

Purpose:

> **What exact content was installed when this save was written?**

A changed `content_hash` means content differs.

It does **not** automatically mean the world baseline differs.

It must not be an input to unrelated RNG draws.

---

## 3.5 `worldgen_version`

A human-controlled compatibility epoch for the world-generation contract.

Purpose:

> **Did the project intentionally change the meaning/algorithm of baseline generation?**

This remains useful for:

- migration registration;
- diagnostics;
- grouping compatible generator behavior.

But a human-edited integer is not enough by itself to prevent accidental baseline drift.

---

## 3.6 `worldgen_fingerprint`

Add a computed diagnostic fingerprint for the generation implementation/configuration.

It should be derived from the actual baseline-generation contract as reproducibly as practical.

Potential inputs include:

- generation assembly/code identity;
- baseline-generation configuration;
- RNG contract version;
- generator-owned tables/data;
- deterministic numeric/config policy.

It is a detection mechanism, not procedural entropy.

It must **not** be mixed into every random draw.

If a perfectly portable code hash is impractical, use a reproducible project-generated fingerprint plus canonical probe hashes. The important requirement is that accidental generator drift becomes observable rather than relying only on a developer remembering to increment `worldgen_version`.

---

## 3.7 Per-cell `baseline_hash`

Every persisted changed-cell delta should record the hash of the exact canonical cell baseline against which that delta was produced.

Conceptually:

```yaml
cell_id: region.otherhome_marches.cell_14_28
baseline_hash: "sha256:..."
delta:
  ...
```

This is the strongest local guard.

At load time:

```text
generate current baseline for changed cell
        ↓
hash baseline
        ↓
compare with saved delta's baseline_hash
```

If equal:

> The delta is proven to target the same baseline.

If different:

> A migration/rebase decision is required.

Never silently apply the delta anyway.

This allows an unrelated content change to alter `content_hash` while a changed cell whose actual baseline output is unchanged still loads safely.

---

# 4. Stable Random Channels

M2b must remove whole-pack content identity from procedural entropy.

Avoid a single order-dependent stream such as:

```text
rng.Next()  // tree
rng.Next()  // rock
rng.Next()  // wolf
rng.Next()  // mushroom
```

Adding one new call shifts everything after it.

Prefer semantic random channels.

Conceptually:

```text
Random(
    world_seed,
    cell_id,
    subsystem,
    semantic_key,
    sample_index
)
```

Examples:

```text
Random(seed, cell, "foliage", "oak_slot_017", 0)
Random(seed, cell, "rocks", "ridge_cluster_03", 0)
Random(seed, cell, "wildlife", "spawn_slot_04", 0)
Random(seed, cell, "resources", "ore_slot_09", 0)
```

Properties required:

- deterministic;
- ordinal/culture-independent;
- independent of iteration order;
- independent of unrelated content pack bytes;
- stable when an unrelated subsystem adds a new draw;
- explicitly versioned if the RNG algorithm/encoding changes.

A subsystem may use a local sequence after deriving a stable channel seed if its own contract guarantees fixed semantics, but global generation must not depend on incidental call order.

---

# 5. What Counts as a Content Change?

M2b should classify changes rather than treating every hash mismatch the same.

## Class A — Content-only, baseline-neutral

Examples:

- wolf HP 30 → 31;
- item description text change;
- dialogue typo;
- crafting price adjustment;
- UI text;
- balance value read from a definition at runtime rather than copied into baseline placement.

Expected result:

- `content_hash` changes;
- world baseline placement does not;
- existing cell `baseline_hash` remains equal;
- load proceeds after any required content/schema migration;
- no global reshuffle.

---

## Class B — Definition identity migration

Examples:

```text
creature.wolf
→ creature.grey_wolf
```

or removal of a definition.

Expected result:

- explicit alias/tombstone migration map;
- old saves are migrated deliberately;
- an unresolved persisted reference is a migration failure;
- no fuzzy string matching;
- no silent “closest definition” replacement.

---

## Class C — Baseline-affecting content change

Examples:

- adding/removing a species from a procedural spawn table;
- changing authored terrain placement data;
- changing resource-node generation input;
- changing foliage density/rules;
- altering an authored slot used by baseline generation.

Expected result:

- only affected baseline outputs should change;
- changed saved cells detect mismatch via `baseline_hash`;
- migration policy must decide whether their semantic deltas can be rebased;
- untouched cells may legitimately use the new baseline after upgrade if that is the declared migration policy.

---

## Class D — Generator contract change

Examples:

- new terrain algorithm;
- changing coordinate quantization;
- changing random algorithm;
- changing stable key encoding;
- changing placement semantics.

Expected result:

- `worldgen_version` changes intentionally;
- `worldgen_fingerprint` changes;
- canonical probe hashes change;
- affected deltas require an explicit registered migration/rebase path;
- normal loading refuses if no path exists.

---

## Class E — Save-schema-only change

Examples:

- player state gains a new required field;
- a persisted record is split/reformatted.

Expected result:

- ordered schema migration;
- fixture coverage;
- no baseline change unless the migrated state itself requires one.

---

# 6. Delta Semantics

Sparse persistence should remain **semantic**, not positional.

A delta should describe authoritative divergence such as:

- entity destroyed;
- authored/baseline object state overridden;
- container contents changed;
- player-created instance added;
- persistent property value changed;
- building placed;
- resource depleted until a deadline.

It should not mean:

> “The 17th item generated by an opaque list is modified.”

Where a delta targets baseline-generated content, it needs a stable target identity/anchor.

Potential stable anchor components:

```text
cell_id
generator_namespace
semantic_slot_key
definition_id where relevant
```

Do not depend on collection index or enumeration order.

---

# 7. Baseline Rebase Policy

M2b must define a conservative rebase policy.

## Safe automatic rebase

An old delta may be automatically overlaid onto a new baseline only when all of the following are true:

1. schema migration succeeded;
2. content aliases/tombstones resolved;
3. every delta target has a stable semantic identity in the new baseline;
4. the meaning/type of every overridden field remains compatible;
5. relevant invariants validate afterward;
6. no migration rule marks the change as requiring custom handling.

Example:

A chest's baseline moved from 20 gold to 25 gold, but the player had emptied it and the persisted semantic state is `contents = []`.

If the chest's stable baseline identity still exists, preserving the player's empty chest can be valid.

---

## Unsafe automatic rebase

Do not automatically rebase when:

- target identity disappeared with no map;
- terrain/topology change makes a persisted position illegal;
- baseline object changed kind/meaning;
- slot semantics changed;
- persisted building now intersects invalid terrain;
- old delta is positional rather than semantic;
- migration would require guessing.

Result:

> migration required / save cannot load under this content without an explicit migration.

Fail loudly and diagnostically.

---

# 8. No Silent Baseline Regeneration

The following is forbidden:

```text
content changed
→ regenerate baseline
→ apply old delta anyway
→ hope it still means the same thing
```

The loader must prove compatibility or run an explicit migration.

This is more important than convenience.

A loud migration error is recoverable.

Silent world corruption is not.

---

# 9. Load Decision Matrix

Recommended high-level behavior:

| Condition | Action |
|---|---|
| `save_format` unsupported | Refuse |
| checksums fail in non-quarantinable core data | Refuse/recovery path |
| `schema_version` old with registered chain | Migrate |
| `schema_version` old without chain | Refuse |
| exact `content_hash` match | Continue compatibility checks |
| content hash differs but IDs/schema are compatible | Run content migration/validation |
| cell baseline hash matches | Apply delta |
| cell baseline hash differs + registered safe migration | Migrate/rebase, validate |
| cell baseline hash differs + no migration | Refuse that save/load path; never guess |
| removed/renamed DefinitionId with map | Resolve |
| removed/renamed DefinitionId without map | Refuse / CI failure |

---

# 10. Migration Chain

Implement a deterministic ordered migration registry.

Conceptually:

```text
schema 1
  ↓ Migration_1_2
schema 2
  ↓ Migration_2_3
schema 3
```

Requirements:

- one-directional upgrade path;
- no skipping unknown intermediate versions;
- idempotence where practical;
- deterministic results;
- migration report;
- explicit failure details;
- source save never mutated in place before successful commit.

Migration functions should be testable headlessly.

---

# 11. Historical Fixtures

Create a committed fixture set for **every historical schema version that has shipped into the repository migration contract**.

Suggested layout:

```text
tests/
  Persistence.Tests/
    Fixtures/
      v1/
      v2/
      v3/
```

Each schema-changing commit must:

1. preserve an old-version fixture;
2. add the new expected fixture/state;
3. add/update migration tests;
4. prove every supported historical fixture reaches current state.

Fixtures should be small but semantically meaningful.

Include at least:

- player state;
- an unchanged cell;
- a changed cell;
- a tombstoned baseline entity;
- a created persistent entity;
- a renamed DefinitionId case;
- enough state to detect lossy migration.

---

# 12. Required M2b Synthetic Migration Tests

M2b is not complete until automated tests prove these cases.

## 12.1 Required-field schema migration

Create v1 player state.

v2 adds a required field.

Prove:

```text
v1 fixture → v2 migration → current load
```

with the correct deterministic default/derived value.

---

## 12.2 Multi-hop migration

Prove:

```text
v1 → v2 → v3
```

and compare with a canonical expected current state.

Do not only test latest-minus-one.

---

## 12.3 Definition rename

Persist reference:

```text
creature.wolf
```

Then rename to:

```text
creature.grey_wolf
```

With alias map:

> migration succeeds.

Without alias map:

> CI fails.

---

## 12.4 Definition removal

Test:

- explicit tombstone/discard migration;
- explicit replacement migration;
- unresolved removal failure.

---

## 12.5 Baseline-neutral content change

Change a runtime balance value that should not alter placement, such as a creature stat.

Prove:

- `content_hash` changes;
- canonical cell baseline hash does **not** change;
- saved delta loads without world reshuffle.

This directly protects against the M2 content-hash coupling bug.

---

## 12.6 Stable random-channel isolation

Add a new RNG use to one generation subsystem.

Prove unrelated semantic channels do not change.

Example:

> adding a rock-decoration draw must not move existing wildlife or mushroom draws.

---

## 12.7 Baseline-affecting change

Make an intentional generation input change.

Prove:

- affected baseline hash changes;
- loader refuses to apply an old delta silently;
- a registered migration path can resolve the synthetic case;
- no migration path produces a clear actionable failure.

---

## 12.8 Accidental generator drift

Change generation implementation without incrementing `worldgen_version`.

Prove at least one automated mechanism detects it:

- `worldgen_fingerprint`;
- canonical probe hashes;
- persisted per-cell baseline hash during load.

The system must not rely solely on human discipline.

---

## 12.9 Migration crash safety

Kill/interruption during migration must leave:

- original save intact, or
- fully committed migrated save.

Never a half-migrated primary save.

Reuse the M2 crash-safe commit mechanism rather than creating a separate unsafe write path.

---

## 12.10 Dry-run immutability

`save:migrate --dry-run` must:

- load;
- analyze;
- report;
- perform no persistent mutation.

Test filesystem contents/hashes before and after.

---

# 13. CLI

M2b requires:

```text
save:migrate --dry-run <save>
```

Report at minimum:

- current save format;
- source/current schema version;
- source/current content version/hash;
- source/current worldgen version/fingerprint;
- DefinitionId aliases to apply;
- tombstones/removals;
- changed cells whose baseline hashes match;
- changed cells whose baseline hashes mismatch;
- registered migration steps;
- warnings;
- blockers;
- expected data loss, if any.

Recommended additional command if inexpensive:

```text
save:inspect <save>
```

This is useful but not required to pass M2b unless already compatible with the tooling architecture.

---

# 14. Migration Report

Every migration should be able to produce a structured report.

Example:

```text
Save: MyWorld/manual-003
Schema: 1 → 3
Content hash: changed
Worldgen: 1 → 1

Applied:
- player v1 → v2
- player v2 → v3
- alias creature.wolf → creature.grey_wolf

Cells:
- 8 changed-cell baselines unchanged
- 1 safely rebased
- 0 unresolved

Loss:
- none

Result:
READY TO MIGRATE
```

Do not bury migration behavior in logs only.

---

# 15. Manifest Direction

Exact field names must be reconciled with the M2 implementation rather than rewritten blindly.

The conceptual manifest identity should cover:

```yaml
save_format: 1
schema_version: 3

content_version: "0.x.y"
content_hash: "sha256:..."

world_seed: "..."
worldgen_version: 2
worldgen_fingerprint: "sha256:..."
rng_contract_version: 1
```

Changed-cell records additionally carry their own `baseline_hash`.

If M2 already has equivalent fields under different names, preserve compatibility and document the mapping rather than gratuitously renaming everything.

---

# 16. Compatibility Authority

Recommended authority order:

1. **Save format** — can this loader parse the container?
2. **Integrity/checksums** — are the bytes trustworthy?
3. **Schema migration chain** — can serialized state be interpreted?
4. **Definition ID migration** — can persisted content references be resolved?
5. **Baseline compatibility** — are sparse deltas safe against current generated output?
6. **Invariant validation** — is resulting authoritative state legal?
7. **Commit migrated save** — only after successful validation/load.

Do not recompute derived caches until migration and delta application are complete.

---

# 17. Canonical Probe Set

Retain/expand M2's deterministic baseline test.

Define a small fixed set of canonical probe cells.

For each supported worldgen contract, record expected hashes.

CI should prove:

- same build → same hashes across repeated runs;
- same build → same hashes across separate processes;
- unrelated content-only change → same hashes where baseline should be unaffected;
- intentional baseline change → expected hash change;
- accidental change without migration metadata → CI failure.

This complements per-save baseline hashes.

---

# 18. Content Compiler Responsibility

Longer term, the content pipeline should distinguish:

- data consumed by baseline generation;
- data consumed only at runtime.

Do not implement a complex annotation system solely for M2b unless necessary.

The immediate hard requirement is behavioral:

> changing runtime-only content must not reshuffle unrelated baseline generation.

The stable RNG and baseline-hash tests prove that behavior directly.

---

# 19. M2b Exit Criteria — Updated

M2b is complete only when all of the following pass automatically.

### Migration harness

- ordered schema migrations exist;
- multi-hop migration is tested;
- every historical fixture loads under current code;
- fixture policy is documented.

### Content identity

- DefinitionId rename/removal requires explicit migration mapping;
- missing map fails CI;
- unrelated content change does not globally perturb worldgen RNG.

### Baseline compatibility

- changed-cell deltas record/verify `baseline_hash` or an equally strong proven mechanism;
- baseline mismatch never silently accepts an old delta;
- one synthetic baseline-affecting migration/rebase case is proven;
- accidental generator drift is detectable.

### RNG stability

- random channels are semantic and call-order isolated;
- adding an unrelated random draw does not shift other subsystem outputs.

### CLI

- `save:migrate --dry-run` reports the migration plan;
- dry-run is proven non-mutating.

### Crash safety

- migrated save commit uses the M2 atomic save path;
- interruption leaves old or new valid state, never partial migration.

### CI proof

At minimum CI proves:

```text
v1 fixture
→ v2
→ v3/current
```

with correct non-lossy expected state.

---

# 20. M2b Non-Goals

Do **not** use M2b to implement:

- progression;
- combat;
- inventory gameplay;
- Soul/incarnation persistence;
- multiplayer/networking;
- weather/seasons;
- settlement simulation;
- AI narrative;
- full save-editor UI;
- arbitrary backward migration/downgrade;
- speculative cloud-save synchronization.

M2b exists to make future schema/content evolution safe.

---

# 21. Required Normative Documentation Reconciliation

M2 revealed that several older documents describe world generation as depending directly on the whole `content_hash`.

M2b must reconcile those descriptions.

Review and update, where present:

- `PERSISTENCE.md`
- `SYSTEMS.md`
- `ARCHITECTURE.md`
- `DATA_MODEL.md`
- `DECISIONS.md`
- `ROADMAP.md`
- `RISK_REGISTER.md`
- `M2_STATUS.md`

Specific changes:

1. remove whole-pack `content_hash` from unrelated procedural entropy;
2. define exact semantics of `content_hash`;
3. define `worldgen_version` vs computed generation fingerprint;
4. document per-cell baseline compatibility checking;
5. document stable semantic RNG channels;
6. update load sequence;
7. update RK-01/RK-P01 mitigation;
8. expand M2b exit criteria to include the content-hash/baseline issue found by M2.

Do not silently modify contradictory normative docs independently. Reconcile them as one coherent change.

---

# 22. M2 Acceptance Before M2b

Before implementing M2b, independently audit the just-completed M2 branch.

Confirm:

- full solution test count has not lost prior coverage;
- one canonical DefinitionId contract remains;
- no temporary/debug files survived;
- Entity Registry still owns identity/lifetime only;
- persistence does not expose an unrestricted authoritative-state mutation backdoor;
- ULID/prefix serialization is canonical;
- deterministic output is culture/process independent;
- crash probes run on the intended Windows environment;
- every deviation listed in `M2_STATUS.md` is accepted or corrected.

M2b should begin only after this audit is clean.

---

# 23. Relationship to M2c

M2b and M2c are both downstream of M2, but they solve different risks.

Before M2c implementation, perform the previously identified progression-axis reconciliation/anti-duplication review.

In particular, re-evaluate overlap among:

- generic skills;
- weapon mastery;
- magic mastery;
- professions;
- advancement currencies.

Do this **before** progression schemas become persistent and therefore migration obligations.

M2b must exist first or in parallel so any future progression schema changes are immediately covered by the migration harness.

---

# 24. Definition of Done

The milestone is done when this statement is true:

> **We can intentionally change save schema, rename content, change unrelated balance data, or change baseline generation, and the loader can prove whether an old sparse save is safe, migrate it when a defined path exists, and refuse it loudly when it cannot — without silently reshuffling the world or corrupting player state.**
