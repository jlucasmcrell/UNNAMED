# Adversarial Review — PERSISTENCE.md (Phase 0)

Scope: persistence design only (`D-05` sparse deltas over a deterministic baseline). Grounded in `PERSISTENCE.md`, `DATA_MODEL.md`, `DECISIONS.md`, `RISK_REGISTER.md`, `PROJECT_CHARTER.md`. Findings are ordered by severity. Risks already in `RISK_REGISTER.md` are not restated; where one exists, the **mitigation** is attacked instead.

---

## F-1 — CRITICAL — The sparse delta has no defined relationship to the baseline it is a delta *of*

**Evidence.**

> `PERSISTENCE.md:28` — "Load = `generate(seed, content) + apply(delta) + apply(player_state)`. Save = `diff(store, baseline(seed, content)) + player_state + manifest`."

> `PERSISTENCE.md:151` — "Written **only** when a cell diverges from baseline. Presence implies divergence; absence means "pristine"."

> `PERSISTENCE.md:181` — "Only entities that differ from their spawn baseline appear here."

Every one of these assertions presumes a comparison *against a materialised baseline*. Nothing in the document says what the baseline for a **persisted** record is, when it is computed, or how a record that has diverged is ever recognised as having returned to baseline.

Three concrete holes fall out of that:

1. **No rebase operation exists.** The save is defined as `diff(store, baseline)`, but the saved artifact is itself the only copy of the divergence. The regenerated baseline is never diffed against the *persisted delta*, so a record cannot be retired. Consequences: a node harvested and respawned 400 hours ago is still stored (§5.2 stores `state: depleted, last_harvest_tick` and never gives a rule for removal); a door opened and closed is still stored; a corpse long past decay is still stored unless something deletes it, and §9's only mechanism is "**trim transient entity lifetime**" (`PERSISTENCE.md:391`), which is a lifetime policy, not a rebase. The saving half of §1.2 has never been implemented by anything described in the document.

2. **`dirty_mask` requires a baseline instance that may not exist.** `entities.msgpack` records "`dirty_mask` | Which fields differ from the spawned baseline" (`PERSISTENCE.md:179`). For an entity in a cell that is currently unloaded, or that has been promoted down to Tier C/D, or whose `def_id` was tombstoned at load, there is no spawned instance to diff against. So the diff cannot be computed where §9's size budget actually matters (late game, thousands of diverged instances).

3. **Cross-cell divergence is unrepresentable.** §5.2 keys the cell delta by "`dirty_reasons: [nodes, spawns, flags]`" (`PERSISTENCE.md:155`). A placed chest, a mined node in a cell the player never entered, or a corpse dragged over a boundary is an entity whose *host cell* may be pristine. On load, `generate + apply(delta)` reconstructs a pristine cell with an entity delta pointing into it: no host cell record, no reason code, no defined behaviour. Either the entity is dropped or it lands in a cell whose baseline says nothing should be there.

**Why it matters.** This is the load-bearing mechanism of the whole format, not an edge case. `D-05`'s central claim — "the *unchanged* world costs zero bytes" (`DECISIONS.md:126`) — is only true if divergence is *recomputed*; the document instead relies on a mutation-set that is never reconciled. §9's worst-case budget for `entities` is "2–40 MB (linear in diverged instances: loot, corpses, NPC changes)" (`PERSISTENCE.md:389`) — 40 MB of a delta that nothing ever prunes.

**What I would do instead.** Add an explicit `Rebase(cell | entity)` operation as a first-class persistence primitive, with a stated trigger (cell unload, autosave, or an explicit compaction pass), a defined outcome ("record differs from regenerated baseline by less than epsilon ⇒ delete the record"), and a P1 test asserting delta *shrinkage* after a full baseline-equivalence cycle. Ship the *rebase* path before the delta path, because a delta format without compaction is a leak with a schema.

---

## F-2 — CRITICAL — "Dirty" is a mutation flag, not a divergence predicate, and the document treats them as identical

**Evidence.**

> `PERSISTENCE.md:52` — "**Honest risk.** "Unchanged" is a judgement made by a per-cell dirty flag (§5.2)."

> `PERSISTENCE.md:437` — "| RK-P02 | Dirty-flag completeness | A missed flag silently loses world changes; the save looks valid | Mitigation: one mutation dispatcher owns the flag. Not yet proven sufficient — needs an audit test that mutates every command and asserts a delta appeared |"

> `PERSISTENCE.md:403` — "| T-03 | Delta minimality | Saving twice with no mutation yields an identical `cells`/`entities` section (no delta creep); an untouched cell contributes zero bytes | **P1** |"

The design's stated criterion is divergence (§5.2). Its implemented criterion is *"a command touched this cell"*. These differ in both directions, and only one direction is tested:

- **False positive (byte growth).** Any net-zero mutation — pick up an item and put it back, open and close a door, harvest and re-harvest, build and demolish a wall — leaves the cell marked dirty with a persisted record forever, because nothing un-sets the flag. This makes the delta monotonic in *commands issued*, not in *world state*. T-03 cannot catch it: it only saves twice "with no mutation" between saves, which is the one interleaving that provably cannot produce creep.
- **False negative (data loss).** RK-P02's mitigation is a single dispatcher, and its validation is "an audit test that mutates every command and asserts a delta appeared" (`PERSISTENCE.md:437`). That test is blind by construction to mutations that never pass through a command. §5.5 requires exactly such a class of state: things that are "recomputed as `f(nowTick, persisted node state)`" (`PERSISTENCE.md:49`), plus every abstract-tier (C/D) projection of schedules and coarse position that `RK-12` requires to change with `game_tick` (`RISK_REGISTER.md:236`). If a tier-C NPC's derived position changes without a command, no dispatcher sees it, and the flag audit passes because the audit only exercises commands.

**Why it matters.** RK-11 correctly calls this the worst failure mode in the register (`RISK_REGISTER.md:33`). The mitigation is nonetheless incomplete in the direction the register does not name: the negative test proves commands are covered, not that *non-command state changes are impossible*. And the false-positive direction is unfunded entirely — there is no equivalent of "delta does not grow when the world reverts to baseline", so the failure is invisible until the sizes in §9 are exceeded in real play.

**What I would do instead.** Define the dirty set as *derived at save time* — walk the diverged entities/nodes/overrides for each candidate cell and compare against the regenerated baseline — and demote the per-cell flag to a **performance hint** that only decides which cells are worth diffing. Then write T-03 as an adversarial pair: (a) net-zero mutation ⇒ byte-identical section, (b) a state change applied outside the command path ⇒ round-trip fails loudly (the negative test `RISK_REGISTER.md:214` already asks for, extended past the dispatcher).

---

## F-3 — CRITICAL — Persisted entity ULIDs have no stable key into the regenerated baseline, so the spawner and the delta collide

**Evidence.**

> `PERSISTENCE.md:43` — "| Unmodified creature/NPC instances at spawn defaults | Spawner re-instantiates from the cell's population table |"

> `PERSISTENCE.md:164` — "Node/spawn keys are **derived**, not stored ULIDs, because a resource node is a property of the world rather than an instance the player owns: `node.<cellkey>.<index>`, where `<index>` is assigned by the cell generator. That is what lets a pristine cell cost zero bytes."

> `PERSISTENCE.md:160` — "`pop.c_07_11.wolves: { alive: 2, baseline: 5, last_kill_tick: 918190000, seq: 7 }`"

> `PERSISTENCE.md:172` — "| `instance_id` | ULID (`D-04`); the only cross-reference key used anywhere |"

The document solves baseline identity for **nodes** (derived `node.<cellkey>.<index>` keys) and then abandons that solution for **entities**, which are ULID-keyed with no defined mapping to the baseline population slot they correspond to. Both an entity delta *and* a `spawns` population record can describe the same kill.

Concrete failures:

- A generic NPC killed at tick *t* is (per `PERSISTENCE.md:43`) an NPC at non-default state ⇒ a persisted entity. On load the spawner "re-instantiates from the cell's population table" with no rule suppressing the baseline member that entity replaced. Result: **the dead NPC resurrects**, or the cell now holds `alive: 2, baseline: 5` *plus* a persisted live NPC — dead counts that no longer mean anything.
- `entities.msgpack` has no derived key, so a persisted entity cannot be matched to its baseline slot, cannot be rebased (F-1), and cannot be excluded from the population count. This identity asymmetry between §5.2 and §5.3 is invisible because the two sections never reference each other.
- `RK-A8`, "spawn suppression vs. persistence; mitigated by the diverged-actor rule and worth re-testing if that rule changes" (`RISK_REGISTER.md:293`), was explicitly **not promoted** to the register. `RISK_REGISTER.md:296` then says to re-evaluate promotion if that rule is materially altered — but the diverged-actor rule it depends on is not stated in either `PERSISTENCE.md` or `DECISIONS.md`. A mitigation that exists only as a pointer to itself is not a mitigation.

**Why it matters.** This is a resurrection/duplication class bug that passes every integrity check and every listed test. T-17 asserts "unmodified NPCs regenerate identically (baseline intact)" (`PERSISTENCE.md:417`) — it tests the *unmodified* case, which is the case the design already handles, and says nothing about matching modified entities back to their baseline slot. §7.2's "no orphan ULID reference" validator (`PERSISTENCE.md:337`) also cannot detect it, because both records are internally well-formed.

**What I would do instead.** Give every persisted world entity a derived **baseline slot key** in addition to its ULID (`<cellkey>.<population_id>.<ordinal>`, plus `generation_seq`), store it in the delta, and make load a merge keyed on it: a persisted entity *replaces* its slot rather than coexisting with it. Then promote `RK-A8`-equivalent language into `PERSISTENCE.md` as a normative rule, not a cross-reference to `WORLD_ARCHITECTURE.md`.

---

## F-4 — MAJOR — Write order is specified to the fsync; load order is not specified at all

**Evidence.**

> `PERSISTENCE.md:314` — "### 7.1 Write sequence (follow exactly)"

> `PERSISTENCE.md:48` — "| Derived caches (faction power, settlement wealth, encumbrance, quest index) | Recomputed after load; if cached on disk, marked `derived: true` and discarded on any version or checksum doubt |"

> `PERSISTENCE.md:252` — "Migrations run on a **copy** in a staging directory."

> `PERSISTENCE.md:335` — "| Quarantined section is `cells`/`entities`/`buildings` | Load **without** it, set `flags.quarantined_sections`, and state exactly what was lost… Partial recovery beats none |"

> `PERSISTENCE.md:337` — "| Post-load invariant validation fails | Domain validator (no item in two containers, no orphan ULID reference, no quest bound to a missing instance) → quarantine the offending record set |"

§7.1 gives a numbered, non-negotiable write order. There is **no** corresponding load order, and at least four distinct orderings matter:

1. **Derived-state recomputation vs. migration.** The doc says both "recomputed after load" and, in Scenario A, that values derived from `might` are "NOT recomputed here: they are caches, and the domain layer recomputes them after load" (`PERSISTENCE.md:272`). Migrations mutate `SaveDocument` before the domain store exists, so caches must be recomputed only after the *whole* chain and alias pass have run. Nothing states this, and the obvious implementation — recompute caches per section as it deserialises — produces the stale numbers the comment is trying to prevent.
2. **Alias resolution vs. cross-reference validation.** §6.3 resolves every stored `def_id` through aliases and tombstones, with the "no hit at all → **hard content error**" rule (`PERSISTENCE.md:304`). §7.2's validator quarantines "no quest bound to a missing instance". If validation runs before the alias pass, every renamed ID quarantines player data that would have resolved.
3. **Partial load after quarantine.** §7.2 explicitly loads *without* `entities` or `cells`. That guarantees dangling references by construction — a `buildings` record whose `storage: [container ULID…]` (`PERSISTENCE.md:194`) lived in the quarantined `entities` section, a `spawns` count referring to a persisted corpse, a quest bound to a quarantined instance. The design's own invariant #7.2 ("no orphan ULID reference") is then false *by design* on exactly the path that exists to preserve player data. §6.3 has the same hole from the other direction: tombstone `destroy` marks "any quest objective referencing it … `failed: content_removed`" (`PERSISTENCE.md:302`), but tombstone `quarantine` says only "move to `orphans.msgpack`" (`PERSISTENCE.md:303`) with no disposition for references to it.
4. **`orphans.msgpack` is not in the layout.** §3.2 lists seven files; `orphans.msgpack` appears only in §6.3 and is admitted later as "**optional** section created only on a quarantine event" (`PERSISTENCE.md:445`). An implementer working from §3.2 will not create it; a save with a quarantine event will not have it in `sections.sha256`, and `savetool repair`'s "drop a named quarantined section" (`PERSISTENCE.md:345`) has no defined target.

**Why it matters.** Ordering bugs here are exactly the "another AI session implements persistence from this document alone" failure (`PERSISTENCE.md:8`) — the document is precise where a bug is cheap to find (write order) and silent where a bug is silent (load order).

**What I would do instead.** Add a §7.4 "Load sequence (follow exactly)" mirroring §7.1's style: read manifest → verify `sections.sha256` → deserialise sections → alias/tombstone pass → migration chain → **then** create the world store → generate baseline → apply cell delta → apply entity delta (with F-3's slot merge) → **then** recompute derived caches → **then** run invariant validation, which must be aware of `quarantined_sections` and must downgrade reference errors whose target section was quarantined from "quarantine" to "reported loss".

---

## F-5 — MAJOR — "Generation code is frozen per content version" is unenforceable as written, and the mitigation of RK-01 is self-defeating

**Evidence.**

> `PERSISTENCE.md:105` — `"worldgen_version": 3,        // generation-code version; frozen per content version`

> `PERSISTENCE.md:233` — "`worldgen_version` covers generation code and **must not change inside a content version**: if it does, deltas for affected cells are invalid, and the only remedies are a tested re-anchoring tool or localized promotion to full serialization (`D-05`)."

> `PERSISTENCE.md:436` — "Mitigations: frozen `worldgen_version` per content version, CI cell-hash test, re-anchoring tool scoped to Phase 2, localized full-serialization fallback"

> `DECISIONS.md:130` — "**Hard requirement:** world generation from `(seed, content_version)` must be **deterministic and version-stable**… Mitigations: generation code is frozen per content version…"

Four problems, in increasing order of severity:

1. **`worldgen_version` is a self-reported integer with no derived verification.** `content_hash` is "hash over the compiled content pack" (`PERSISTENCE.md:104`) — it is computed. `worldgen_version` is a hand-edited literal. A session that tunes terrain noise, changes an RNG call order, or "fixes a bug in the vegetation scatter" and does not bump it produces a world where the manifest's own version fields still match, all checksums still verify, and every delta silently applies to the wrong baseline. The CI cell-hash test detects *a* change but the document never says the digest is persisted into the save or checked at load, so the detection exists only inside CI — it does not protect a player's existing save on the user's machine.
2. **The remedies have no schedule and no owner.** "re-anchoring tool scoped to Phase 2" (`PERSISTENCE.md:436`) is the only escape hatch, and `RK-P01` is mapped to an already-registered risk with no promotion (`RISK_REGISTER.md:284`), so the re-anchoring tool appears nowhere in the register's task list or the "First 10 development tasks" (`RISK_REGISTER.md:358`). Between Milestone 1 and Phase 2, the ONLY remedy is the "localized promotion to full serialization" fallback — for a 1–2 person team that means either freezing the generator entirely (real progress stops) or accepting silent corruption.
3. **`content_version` numbering is unspecified and the manifest mixes regimes.** `DATA_MODEL.md:627` says: "**Content-version numbering** (semver vs. monotonic integer) is left to `PERSISTENCE.md`; this document requires only that it is a single value recorded in both the save manifest and the compiled content cache." `PERSISTENCE.md` never answers it, and the manifest shows `"content_version": "0.4.2"` (`PERSISTENCE.md:103`) alongside `"schema_version": 17` and `"worldgen_version": 3` — three different numbering regimes. But §6.1 makes load-time behaviour depend on version *relationships*: "equal hash means proceed with no migration … a differing hash runs the alias/tombstone pass (§6.3) plus the chain if `schema_version` also differs" (`PERSISTENCE.md:233`). A bounded-set rule for a free-form string is unimplementable as written, and the deferred question is delegated to a document that does not answer it.
4. **The overlap between `content_hash` and `worldgen_version` is unreconciled, and the "frozen" rule has a documented exception that the format does not survive.** §6.1 says `worldgen_version` "**must not change inside a content version**"; §5.5 says "Respawn is a pure function of persisted data plus world time, so loading cannot change the answer" (`PERSISTENCE.md:210`). Both claims are things the format cannot detect being violated. A change to `respawn_window(node_def)` is content, not worldgen: `content_hash` changes, prose says that "runs the alias/tombstone pass … plus the chain" (`PERSISTENCE.md:233`) — and the chain will not fix a `last_harvest_tick` that was stored against a different window, so `ready_tick` silently moves.

**Why it matters.** RK-01 is rated High/Critical and the register's validation is a CI digest test. This review's finding is narrower and worse: **the mitigation only fires in CI, on commits touching generation, in a repo where an AI session edits generation code.** Nothing in the save format or the load path can distinguish a correct load from a mismatched-baseline load. That is precisely the "silent corruption of every save simultaneously" outcome `RISK_REGISTER.md:23` describes, and the described mitigation does not cover it.

**What I would do instead.** Make the baseline itself content-addressed: compute a `worldgen_digest` over the generation assembly + the placement data + the runtime/FMA configuration, store it in the manifest, and verify it at load *before* applying any cell delta. On mismatch, refuse or offer the §6.1 fallback explicitly — never apply. `worldgen_version` then becomes a human label for the digest rather than the authority. Also move the re-anchoring tool into Milestone 2 as a task, not a Phase-2 scoping note, since it is the only thing standing between a generator tweak and a wiped save.

---

## F-6 — MAJOR — Offline catch-up policy (bounded window + clamp) is mathematically incompatible with §5.5's pure-function rule, and no test covers the interaction

**Evidence.**

> `PERSISTENCE.md:210` — "Respawn is a pure function of persisted data plus world time, so loading cannot change the answer."

> `PERSISTENCE.md:211` — "Time not played still advances world time (abstract catch-up), so a node harvested before a session break is correctly regrown on return."

> `RISK_REGISTER.md:236` — "Catch-up is applied in bounded chunks with a **per-load cap**, and the cap is a *policy* to be decided by the owner rather than discovered by a player. Per-cell and per-population values are clamped to authored `[min, max]`…"

> `RISK_REGISTER.md:390` — "**Default if unanswered:** honour a bounded catch-up window, clamp all abstract values to their authored `[min, max]`, and never present a summarised "while you were away" report in Phase 1–2."

> `PERSISTENCE.md:422` — "| T-22 | Spawn/respawn consistency | A harvested node's ready time is identical across save/load and across a simulated offline gap (§5.5) | P2 |"

A capped catch-up window plus clamping makes world state a function of **absence history**, not of `(state, tick)`:

- Absent 300 days in one stretch vs. 300 absences of one day each produce different totals if the cap is per-load, and the same world tick. §5.5's "loading cannot change the answer" is then false for any value that touches an accumulator (population, merchant stock, economy counters, node respawn).
- The stronger invariant `RK-12` asks for — "running 300 days in one step equals running 300 single-day steps" (`RISK_REGISTER.md:232`) — is *violated by design* once the cap exists, because a single 300-day step is clamped and 300 one-day steps are not. `RK-12`'s validation would report a pass on the clamp path and a fail on the chunk path with no way to tell which is correct.
- T-22 tests a node's ready time "across a simulated offline gap" (`PERSISTENCE.md:422`) — a single gap of unspecified length, at P2. It cannot detect the cap/chunk/absence-history interaction, and it does not cover the `pop.*.alive/baseline` counters in §5.2 at all.
- `PERSISTENCE.md:214` records the adjacent open problem ("if respawn should later depend on *world* state…"), and `RK-P05` frames the constraint as "anything not derived from `(state, tick)` breaks" (`PERSISTENCE.md:440`) — but neither notices that the sanctioned catch-up policy is itself such a thing.

**Why it matters.** This is a mitigation of an already-registered risk (RK-12) that does not achieve the property the persistence document requires for delta validity. Nodes and populations are exactly what the delta stores (§5.2), so a non-pure catch-up means the stored `last_harvest_tick`/`baseline` values are interpreted differently depending on how the player's calendar looked.

**What I would do instead.** Make catch-up a **pure function of the total `tick_delta`**, applied in chunks only as an implementation detail (chunk size must not be observable), and move the "how much time is honoured" decision from a per-load cap to a *single recorded world-tick advance* in the manifest, so the honored amount is itself persisted state rather than a load-time policy. Then extend T-22 to assert one-step ≡ N-step at P1, not P2.

---

## F-7 — MAJOR — The Windows atomic-commit mitigation covers the crash, not the three things that actually break this on Windows

**Evidence.**

> `PERSISTENCE.md:441` — "| RK-P06 | Windows atomic-rename semantics | POSIX rename-over-directory does not exist on Windows | Open problem: §7.1's three-step commit is untested on the target OS. Validate first during implementation |"

> `RISK_REGISTER.md:250` — "Begin a save, terminate the process at a randomized point mid-commit, relaunch, and assert that a valid save (either the old or the new, never neither) loads."

> `RISK_REGISTER.md:254` — "Write to a staging path and commit via the platform's atomic primitive, verified empirically rather than assumed… `PERSISTENCE.md` §7's integrity table and quarantine path are the fallback if atomicity cannot be achieved"

> `PERSISTENCE.md:380` — "| `.bak-<slot>` | 1 per slot | Previous good contents, retained until the next successful write |"

The documented validation is a **process-kill test**. On the stated baseline platform that test can pass completely while the save system still fails in play, because the real hazards are not crashes:

1. **Sharing violations from other processes.** `Directory.Move`/`File.Replace` under Windows throws `IOException` when any handle is still open on a file in the tree — antivirus real-time scanners and Windows Search indexer open newly written files routinely. §7.1's sequence has no retry, no backoff, and no failure classification; "atomic rename" is not atomic against a third-party handle, it just fails. A `IOException` at step 5b leaves the slot renamed to `.trash-<ulid>` with the staging directory un-promoted: the player has no slot until the boot sweep runs.
2. **Cloud-sync folders.** §3.2 gives the layout as `saves/<profile>/<slot>/` (`PERSISTENCE.md:65`) with no statement of where `<profile>` lives. On Windows the default user-data location is under the user profile, which on a very large fraction of consumer machines is OneDrive-redirected. OneDrive/Dropbox actively renames, holds, and synthesises placeholder files: the rename dance, `.trash-*` deletion, and `boot_state.json` sweep all interact badly with a sync engine that can resurrect a deleted directory or hydrate a placeholder where a real file is expected. This is not covered by `RK-13` at all, and it is the single most likely field failure of this design on the target platform.
3. **The backup is not as protective as the table implies.** `.bak-<slot>` is "retained until the next successful write" (`PERSISTENCE.md:380`). A player who saves three times in a row after a silent content/version problem has three bad saves and one good `.bak` that the next *successful* write discards. Combined with step 7's ordering — "Only after 6 succeeds: update rotation metadata and the "last good save" pointer" (`PERSISTENCE.md:322`) — the previous save's usable lifetime is one commit, not "at least one rolling backup slot" in any meaningful recovery sense.

**Why it matters.** `RK-13`'s impact statement is "a crash mid-save can destroy the *previous* good save" (`RISK_REGISTER.md:243`). The mitigation as written tests only the crash. AV-induced `IOException` and sync-engine interference are *more* frequent than power loss and are guaranteed on the default Windows user profile.

**What I would do instead.** Specify the commit as a retry loop with bounded exponential backoff on `IOException`, distinguish "transient lock" (retry) from "policy denial" (surface to the player), place the save root under a location excluded from known sync roots (or detect and warn), and keep **two** generations of `.bak` with the newest only retired after a verified load, not a verified write.

---

## F-8 — MAJOR — The RNG model has three mutually inconsistent formulations; only one of them is sound, and the save's soundness depends on which

**Evidence.**

> `PERSISTENCE.md:210` — "…all RNG is **stream-based with a persisted counter**, never `Random()` seeded at load, and advancing a stream consumes it permanently in the save."

> `PERSISTENCE.md:111` — `"rng": { "loot": 882140, "spawn": 55102, "encounter": 9123, "cosmetic": 40211 },`

> `DATA_MODEL.md:126` — "anything that must be replayable (loot rolls S-16, spawn picks S-31, weather S-37) consumes a seeded RNG stream derived from `(world_seed, definition_id, instance_or_cell_id, purpose)` rather than persisting the roll."

> `DATA_MODEL.md:563` — "roll seed = (world_seed, item_ulid, "affix")."

Three different mechanisms are described for the same problem:

- **Keyed derivation** (`DATA_MODEL.md:126`, `:563`): the roll is a pure function of `(world_seed, def_id, instance_or_cell_id, purpose)`. Under this model **nothing needs to be persisted**, ordering is irrelevant, and the manifest's `rng` block is dead weight.
- **Global stream counter** (`PERSISTENCE.md:111`): four integers in the manifest. Under this model every draw is order-dependent, so a single draw that happens in a different order after load (a cell loaded sooner, an entity created by a migration, a companion recruited earlier) permanently shifts every subsequent draw in that stream — including loot already sitting in the world.
- **Per-cell stream** (`PERSISTENCE.md:41`): "Same generator, cell-local RNG stream seeded by cell key" — which is the keyed model again, restricted to worldgen.

The document does not say which applies to loot, spawn, encounter, and cosmetic draws. `T-19` asserts "Generated loot is reproducible from persisted streams; reload cannot reroll a chest" (`PERSISTENCE.md:419`) — under the global-counter model that test *fails by design* the moment load order changes, and under the keyed model the counters are unnecessary. There is also no `worldgen`/`terrain` stream in the manifest's `rng` object even though generation is the one place determinism is load-bearing, and no stated relationship between the four counters and "advancing a stream consumes it permanently" (whose stream? per what scope? by what increment?).

**Why it matters.** RNG is the mechanism by which "the unchanged world costs zero bytes" is allowed to be true — loot is regenerated, not stored. If the mechanism is a shared mutable counter, then the sparse delta format's correctness depends on *load order and draw order being identical forever*, which nothing in the document guarantees.

**What I would do instead.** Pick the keyed model outright (`hash(world_seed, purpose_key)` with no shared state), delete the `rng` counter block from the manifest, and keep counters only where a genuine sequence is required (e.g. repeated draws from one container), scoped to that container's ULID rather than globally. Then state the model once, normatively, in both documents.

---

## F-9 — MINOR — `PERSISTENCE.md` and `DATA_MODEL.md` contradict each other on the content-ID migration mechanism

**Evidence.**

> `PERSISTENCE.md:284` — "`# content/aliases.yaml — append-only; entries are never deleted`"

> `DATA_MODEL.md:31` — "`_tags.yaml      # closed tag vocabulary      _aliases.yaml   # ID migration map (append-only)`"

> `DATA_MODEL.md:80` — "```yaml\naliases:                                              # renamed IDs, kept forever\n  item.weapon.ironsword: item.weapon.iron_sword\nremoved:                                              # merged/removed IDs, mapped forward\n  quest.artifact.shattered_crown.09: quest.artifact.shattered_crown.10\n```"

> `DATA_MODEL.md:87` — "A save referencing a removed ID must resolve through `removed` or fail load loudly — never a silent drop (D-05)."

> `PERSISTENCE.md:302` — "4. Tombstone `destroy` → remove the instance and log; any quest objective referencing it is marked `failed: content_removed`, never silently completed."

Two filenames (`content/aliases.yaml` + `content/tombstones.yaml` vs `content/_aliases.yaml`), two schemas (list of `{old,new,since}` records plus a tombstone file with `disposition`/`to` vs a two-key map `aliases:`/`removed:`), and two incompatible semantics for a removed definition — `DATA_MODEL.md:87` says a save referencing a removed ID must **fail load loudly**, while `PERSISTENCE.md:302` sanctions removing the instance and failing the *quest objective* instead. Both documents are named as normative for the implementer (`PERSISTENCE.md:5`, `DATA_MODEL.md:5`). Also: `DATA_MODEL.md:601` names the axis `save_version` and `PERSISTENCE.md:102` names it `schema_version`, while `schema:` (`DATA_MODEL.md:54`) means something else entirely.

**Why it matters.** These are the first files a fresh implementing session writes. The ambiguity is cheap now and expensive once a save exists — and `RK-03`'s whole mitigation is "renames ship only as migration-map entries plus an alias" (`RISK_REGISTER.md:90`), which requires one agreed format.

**What I would do instead.** Make `DATA_MODEL.md` the single owner of the alias/tombstone file format (it already owns content shape), have `PERSISTENCE.md` cite it by section, and rename the manifest field to `state_schema_version` to stop colliding with content's `schema:`.

---

## Risks MISSING from `RISK_REGISTER.md`, specific to persistence

1. **No baseline rebase/compaction subsystem, and therefore no bound on delta growth.** `RK-P02` covers *missing* flags; nothing covers *un-retired* records or delta growth over a long playthrough. `RK-P04`/`RK-06` bound only the buildings term; `PERSISTENCE.md:389`'s `entities` ceiling of 40 MB has no mechanism behind it once `§9(1)`'s lifetime trim is the only lever. Highest-value addition to the register.
2. **Entity-to-baseline identity mapping and spawn-vs-entity collision** (F-3). The closest entry, `RISK_REGISTER.md:293` (`RK-A8`), is explicitly not promoted and defers to an undefined "diverged-actor rule".
3. **Partial-load (quarantine) semantics: referential integrity is knowingly violated, and quarantined records have no reference disposition.** `RISK_REGISTER.md:288` demotes `RK-P08` as "a presentation concern, not architectural". It is not — it collides with `PERSISTENCE.md:337`'s orphan-ULID validator, with `buildings.storage` references into a quarantined `entities` section, and with `orphans.msgpack` not existing in the documented layout.
4. **Windows non-crash commit failures: AV/indexer sharing violations, and save paths inside OneDrive/Dropbox.** `RK-13`'s validation is a process-kill test and cannot detect either.
5. **The persistence substrate as a Phase-1 schedule risk in itself.** Serialization + hash verification + migration chain + fixtures + crash injection + two CLI tools + corruption recovery is a multi-month build for 1–2 people, it is a hard prerequisite for every system "from its first commit" (`PERSISTENCE.md:14`), and Milestone 1 bundles the full design "manifest, player/companion state, and per-cell delta versus baseline" plus atomic write and rolling backup (`RISK_REGISTER.md:371`) into one deliverable. `RK-10` covers project scope, not the fact that the persistence *design as specified* has no MVP reduction and no deferral list. A "Phase-1 persistence subset" (full serialisation of touched cells, no chain, no quarantine, no orphan file) is not written down anywhere, so nothing prevents the full design from being implemented first.
6. **RNG draw-order sensitivity** (F-8). Not in the register, and it is the mechanism that makes "loot is regenerated, not stored" safe.
7. **Nan/float canonicalisation and cross-process byte equality.** `T-02` asserts generation "yields a byte-identical baseline across processes and 100 runs" (`PERSISTENCE.md:402`) while `T-01` asserts "Full authoritative-state equality after load" (`PERSISTENCE.md:401`); the documents never state a numeric type policy (float vs double), a float comparison policy for T-01/T-03, or whether `System.Math` transcendental results are canonicalised. Rigid-body drift, positional epsilon and golden-fixture stability in §10 all sit on this unstated rule.
