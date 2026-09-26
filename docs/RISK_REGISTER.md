# RISK_REGISTER.md — Phase 0 Risk Register

**Project:** Otherreach (codename UNNAMED) — full-body third-person with seamless first-person zoom, solo-first, open-world fantasy RPG
**Phase:** 0 — Vision and Architecture
**Authority:** `PROJECT_CHARTER.md` is the authoritative creative vision. `DECISIONS.md` records settled architecture as `D-01`..`D-12`; this file never contradicts a decision — it records what could still go wrong *given* those decisions.
**Audience:** another AI coding session implementing Phase 1 from these documents alone.
**Produced for:** `PHASE_0.md` STEP 18 (ten greatest risks, with earliest inexpensive validation for each).

## How to read this file

- All project-level risks are numbered `RK-01`..`RK-16` and this file is the **single risk authority**. `RK-01`..`RK-10` are the ten risks `PHASE_0.md` STEP 18 requires. `RK-11`..`RK-14` were promoted here from the local risk tables in `PERSISTENCE.md` (`RK-P01`..`RK-P13`) and `WORLD_ARCHITECTURE.md` (`RK-A1`..`RK-A9`): those documents keep their local IDs for detail, and the four promoted here were the ones with **no** project-register equivalent (see §"Promoted from the document-local risk tables"). `RK-15` and `RK-16` were added by the Phase-0 adversarial review (`REVIEW.md`) and cover enforcement gaps the original ten did not: cross-slice mutation inside the domain, and read-path correctness. `RK-01` is fixed by cross-reference: `D-05` already cites it as the world-generation determinism / sparse-delta baseline risk, so it must be that and nothing else.
- **Likelihood** and **Impact** are assigned only where the *Why it matters* text justifies them in a sentence. No risk appears here for being generically true of software.
- **Earliest inexpensive validation** is the load-bearing field in each detail block: every entry is a concrete spike, test, or measurement cheap enough to run **during Phase 1 or earlier**, with an explicit *fails if* condition written beside it so the result cannot be read optimistically. If a validation needs a finished vertical slice to be meaningful, it is written as the smallest Phase-1 proxy that still distinguishes pass from fail. Read the ten validations as the Phase-1 risk burn-down list.
- **Owner** is the role accountable for running the validation and reporting its result. At current team size all roles resolve to the same one or two people; the column exists so a fresh session knows *who must be told*, not to imply a staffed org.
- Likelihood is honest, not reassuring. A **High** likelihood on a risk we are accepting anyway is the correct entry; manufacturing a **Low** would be the failure mode this document exists to prevent.

## Summary matrix

Likelihood and impact as assessed in this phase. Justification for each rating, the validation action, and the mitigation are in the detail blocks that follow; the ratings are not assertions, they are conclusions of the *Why it matters* reasoning.

| ID | Risk | Likelihood | Impact | Owner |
|---|---|---|---|---|
| **RK-01** | World-generation determinism breaks, so sparse-delta saves corrupt (`D-05`) | **High** — any edit to terrain noise, resource scatter, spawn placement, or generation-time RNG call order changes the baseline; several such edits are certain during Phase 1, when the generator is being written. | **Critical** — corruption is silent and can invalidate every player save simultaneously. `D-05` also makes the *revisit* condition a fallback rather than a fix: promoting one subsystem to full serialization is a contained degradation, but only if we detect the divergence. | Technical Director |
| **RK-02** | Godot cannot hold the representative player-camera frame budget in a 2×2 km slice (`D-01`) | **High** — we are writing bespoke streaming and crowd systems on an engine with no World Partition or Mass AI equivalent; first-pass implementations of exactly this kind of work normally miss budget. | **Critical** — this is the one remaining decision that can restructure the entire codebase, and it is the only condition under which the engine debate legitimately reopens. | Lead Gameplay Engineer |
| **RK-03** | Definition IDs are a public API and get renamed/deleted after saves exist (`D-04`) | **High** — `D-04` itself forecasts this ("before the first content pack ships"), and content churn is highest exactly now, when volume is near zero and every rename is still cheap. | **High** — silent content loss in a save is a trust-destroying class of bug and the charter's SAVE SYSTEM section requires versioning and migrations from early development. Critical-adjacent, stopped short of Critical only because `D-03`'s load-time validator converts most of it from a runtime crash into a startup error. | Lead Systems Designer |
| **RK-04** | The closed objective-type set cannot express real quests, so per-quest code returns (`D-07`) | **Medium–High** — the charter's quest shapes (crafting, construction, faction state, relationships, puzzles, hidden, timed, world-state) are genuinely diverse, and this class of predicate framework usually leaks on the first three real quests. | **High** — epic multi-stage quests are the charter's defining content; if they need hand code, `D-12`'s region-by-region plan cannot scale and the promise of story-driven artifact rewards degrades. | Lead Systems Designer |
| **RK-05** | Companion AI is unreliable, or powerful enough to trivialize combat | **High** — companion behaviour spans navigation, combat targeting, inventory, dialogue, morale, and the `D-06` tier handoff simultaneously, and is the single most commonly shipped-broken subsystem in RPGs of this shape. | **Critical** — companions are load-bearing for solo-first design *and* for the RECRUIT loop; a failure here reduces the game to an emptier, lonelier version of the intended experience. | Lead Gameplay Engineer |
| **RK-06** | Player-built structures fail to round-trip, or become un-navigable | **Medium** — the piece model itself is low-risk because `D-08` deliberately avoided physics; the risk concentrates in navmesh rebuild on placement and in save-size behaviour at hundreds of pieces per cell. | **High** — building is a top-tier retention loop and the charter's settlement fantasy depends on it; a home that half-loads or that companions cannot walk through fails a headline promise. | Lead Gameplay Engineer |
| **RK-07** | Tier transitions produce visible NPC discontinuity (`D-06`) | **Medium** — the mitigation is pre-specified and narrow, but the failure is observational: it shows up only under real player movement patterns, not in unit tests. | **High** — this is the charter's "world that does not revolve around the player" pillar made visible. One teleporting merchant is enough to make the whole world read as fake, which retroactively invalidates the abstraction the project depends on for performance. | Lead Gameplay Engineer |
| **RK-08** | The content pipeline cannot author at target scale | **High** — YAML schemas are verbose, and without linting, schema dump, and ID cross-reference tooling, authoring cost per item stays high enough that the content target is never reached. | **High** — insufficient content is the quietest way this project fails: the systems all work, the vertical slice is fine, and the shipped world is thin. It is also the risk most likely to be under-reported because it produces no error and no crash. | Lead Systems Designer |
| **RK-09** | Local-player assumptions silently destroy the server-shaped seam (`D-02`, `D-11`) | **Medium** — the boundary is structurally enforced (separate `.csproj` with no Godot reference per `D-02`), which blocks whole classes of violation, but the remaining violations are logical, not structural: writing state from a view, or keying anything off "the player" as a global. | **High** — if this fails, the cost lands on the one milestone the charter explicitly refuses to build now, and the honest remedy is a rewrite of every gameplay system the charter warns about. | Technical Director |
| **RK-10** | Scope too large for a small AI-assisted effort | **High** — this is the default outcome for projects of this stated ambition; nothing about our approach changes the base rate, only the discipline applied against it. | **Critical** — it does not damage one system, it prevents the charter's actual goal ("If planning is sufficient, BUILD"). Everything else on this register becomes academic if no playable build ever ships. | Technical Director |
| **RK-11** | Dirty-flag completeness: a missed flag loses world changes while the save still validates (`D-05`) | **Medium–High** — the flags are set in one place by design, but "one place" is a convention that holds only while every mutation path is remembered; the first direct state write from anywhere else defeats it silently. | **Critical** — this is the worst failure mode in the register: the save loads cleanly, passes integrity checks, and has simply *forgotten* a mined node or a killed boss. There is no crash and no error, so it surfaces as player-reported "my progress vanished" long after the change. | Technical Director |
| **RK-12** | Offline catch-up: world time advances across a save boundary and abstract tiers must converge (`D-06`) | **High** — every load is an offline catch-up, and a long absence produces a very large `game_tick` delta that must be resolved without hitches or absurd outcomes (a settlement that has "produced" for 300 days). | **Medium–High** — bounded wrongness is tolerable, but unbounded abstract outcomes are not: a wiped spawn population or a flooded economy both read as a broken world, and both are invisible until playtest. | Lead Systems Engineer |
| **RK-13** | Atomic save commit is untested on the target OS (Windows rename-over-directory) | **Medium** — the three-step staging-then-rename commit is written in POSIX terms; `PERSISTENCE.md` `RK-P06` records it as untested on Windows, which is the stated baseline platform. | **High** — this is the mechanism behind the corruption-recovery guarantee. If the commit is not atomic on the real filesystem, a crash mid-save can destroy the *previous* good save rather than preserving it. | Technical Director |
| **RK-14** | Navmesh stitching at cell seams under player-built structures (`D-08`) | **Medium** — `D-08` guarantees pieces snap, but says nothing about the navmesh across a cell boundary; `WORLD_ARCHITECTURE.md` `RK-A2` records the case as explicitly unsolved. | **High** — a broken navmesh does not announce itself; companions and settlement NPCs simply path wrong or stall, which is exactly the "frustrating rather than helpful" companion failure the charter singles out. | Lead Gameplay Engineer |
| **RK-15** | Cross-slice state mutation inside the domain erodes the authority boundary (`D-02`, `D-10`) | **Medium–High** — the presentation→domain seam is enforced by a compile error, but the interior `Domain`→`Domain` boundary is not; a system writing another system's component compiles, passes every listed test, and produces a plausible diff. | **High** — `ARCHITECTURE.md` calls this "the most common way authority boundaries rot". If it erodes, the cost lands on the one milestone `D-12` refuses to build now, and the honest remedy is a rewrite of the gameplay systems the charter warns about. | Technical Director |
| **RK-16** | The load path is specified three incompatible ways, and read-path correctness is unowned (`D-05`) | **High** — `ARCHITECTURE.md` applied deltas before baseline regeneration; `SYSTEMS.md` and `PERSISTENCE.md` after. Only the *write* sequence was ever written as "follow exactly". | **High** — the natural reading produces stale derived caches and dangling references, and it stays invisible until a save is old enough to migrate. Every other persistence risk concerns what is *written*; none covered *reading*. | Technical Director |

## Risk detail

Each block carries the full case: why the risk matters, the earliest inexpensive validation, and the mitigation. **Earliest inexpensive validation is the load-bearing field** — every entry is a concrete spike, test, or measurement cheap enough to run during Phase 1 or earlier, and it is written as the smallest proxy that still distinguishes pass from fail from the phase in which it is meaningful. Read the ten validation fields as the Phase-1 risk burn-down list.

### RK-01 — World-generation determinism / sparse-delta baseline

**Likelihood:** **High** — any edit to terrain noise, resource scatter, spawn placement, or generation-time RNG call order changes the baseline; several such edits are certain during Phase 1, when the generator is being written.  
**Impact:** **Critical** — corruption is silent and can invalidate every player save simultaneously. `D-05` also makes the *revisit* condition a fallback rather than a fix: promoting one subsystem to full serialization is a contained degradation, but only if we detect the divergence.  
**Owner:** Technical Director

**Risk.** **World-generation determinism cannot be held version-stable, so sparse-delta saves (`D-05`) apply to a baseline that no longer matches.**

**Why it matters.** Per `D-05`, unchanged world costs zero bytes and a cell is persisted only when it diverges from its deterministic baseline. That makes determinism a *hard prerequisite* of the save format, not an optimization: if the baseline tuple (`PERSISTENCE.md` §1.3) stops reproducing the same world, every existing save silently interprets deltas against the wrong cells and corrupts. This is the most dangerous constraint in the project, and `D-05` already names it `RK-01`.

**Earliest inexpensive validation.** Headless test in Phase 1: generate a 2×2 km region twice from one fixed seed and byte-compare a stable digest of (cell terrain hashes + sorted spawn/entity list). Then rerun it across two *deliberately trivial* changes:
- a change to a **generation input** (placement data or generator code), to prove the change is detectable rather than silent;
- a change to **runtime-only content** (a creature's stats), to prove it does *not* move the world.

No engine, no art, no gameplay systems needed — this can be the first test in the repository.

**Fails if.**
- The two digests differ.
- The trivial generation-input change produces *no* detectable difference. This is the more dangerous outcome: it means the check is blind, not that the world is stable.
- The runtime-only content change moves the baseline. M2's first cut did exactly that: it keyed generation on the whole `content_hash`.

**Mitigation (as implemented through M2b).**
- Content identity is not a generation input, and randomness is addressed by semantic key, so unrelated edits and new draws move nothing (`WORLD_ARCHITECTURE.md` §3.4).
- Every changed-cell delta records its `baseline_hash` and is applied only to that baseline, or through a registered transition (`PERSISTENCE.md` §6.4).
- `worldgen_fingerprint` includes the output of four canonical probe cells; the probe digest, the fingerprint and a region digest are pinned in CI against an independent implementation, so an unversioned generator edit fails CI.
- The seed is recorded in the save manifest (`D-05`).
- `D-03` content is validated at load, so a definition typo fails loudly at startup, not mid-quest.

**Status: measured.** M2 proved generation bit-stable across 100 runs and across processes. M2b re-measured it under RNG contract 2, pinned to an independent Python implementation, and added culture-independence tests (`M2_STATUS.md`, `M2B_STATUS.md`). Still to observe: the first Linux CI run.

**Alignment note.** `PERSISTENCE.md` makes `worldgen_version` an explicit field distinct from the content version: generation may change only via a version bump plus a registered transition, never by editing generation code under an unchanged version. M2b added per-cell proof, so an unversioned edit is caught even when nobody remembers the rule.

### RK-02 — Engine frame budget in a 2×2 km slice

**Likelihood:** **High** — we are writing bespoke streaming and crowd systems on an engine with no World Partition or Mass AI equivalent; first-pass implementations of exactly this kind of work normally miss budget.  
**Impact:** **Critical** — this is the one remaining decision that can restructure the entire codebase, and it is the only condition under which the engine debate legitimately reopens.  
**Owner:** Lead Gameplay Engineer

**Risk.** **Godot 4.x cannot sustain the representative player-camera frame budget in a 2×2 km slice once streaming, LOD, foliage, and crowd abstraction exist** — which is `D-01`'s own revisit trigger #1. The representative camera is the full-body third-person view, the first-person view and camera obstruction, never only the cheapest path (`CAMERA_PERSPECTIVE_AND_PRESENTATION.md` §23).

**Why it matters.** `D-01` selected Godot knowing its rendering ceiling is lower than UE5, and explicitly made "we have measured, not guessed" the bar for reopening the engine. The consequence accepted in `D-01` is that world streaming, LOD management, crowd abstraction, and layered simulation are **our** responsibility (`D-06`, `WORLD_ARCHITECTURE.md`). If the measured budget fails, the remedy is the most expensive possible change: engine migration after code exists.

**Earliest inexpensive validation.** Phase-1 greybox stress scene, **not** a beautiful one: 2×2 km of untextured heightmap plus a few hundred instanced proxies, a navmesh, the `D-06` tier scaffold, and 50+ tier-A actors. Measure frame time at the target baseline. Greybox is the point — a dressed scene measures the art budget and hides the systems budget.

**Phase-1 gate (owner ruling, 2026-09-23).** The baseline machine is RAZER's RTX 4070 Ti at 1080p, measured with OBS, H3 and other significant GPU workloads stopped. The gate is the PROTOTYPE greybox scene at a sustained 60 FPS, with CPU and GPU frame times, 1% lows, RAM/VRAM and hitches recorded. The isolated 2×2 km greybox is captured and recorded too, but it is **not** the formal `D-01` revisit gate in Phase 1: that trigger needs the streaming and LOD systems. Failure to sustain 1080p / 60 FPS on the clean 4070 Ti after reasonable optimization, in either scene, is an owner-review stop. The fully dressed `ENGINE_VALIDATION.md` scene is later work.

**Fails if.** Frame time at the target baseline misses budget in the greybox scene with streaming and tier scaffolding active. The number produced is the deliverable either way — `D-01` requires a measurement, so a missing number is a worse outcome than a failing one.

**Mitigation.** Instrument frame time from the first playable commit so the number is never unknown; keep the world-streaming layer behind a narrow interface so a replacement engine path is contained; treat the greybox stress scene as a regression gate that runs as part of the milestone, not a one-off.

### RK-03 — Definition IDs as a public API after saves exist

**Likelihood:** **High** — `D-04` itself forecasts this ("before the first content pack ships"), and content churn is highest exactly now, when volume is near zero and every rename is still cheap.  
**Impact:** **High** — silent content loss in a save is a trust-destroying class of bug and the charter's SAVE SYSTEM section requires versioning and migrations from early development. Critical-adjacent, stopped short of Critical only because `D-03`'s load-time validator converts most of it from a runtime crash into a startup error.  
**Owner:** Lead Systems Designer

**Risk.** **Definition IDs are a public API (`D-04`) and will be renamed, retyped, or deleted after saves exist.**

**Why it matters.** `D-04` makes definition IDs stable forever once shipped and states that renaming requires a migration map — which means the ID namespace is an external contract the moment one real save exists. Content authored by AI sessions is *especially* prone to renames, because a later session will find `item.weapon.iron_sword` inelegant and rename it innocently. Result: quests wait forever on a deleted predicate, or a player's sword vanishes into a null definition.

**Earliest inexpensive validation.** Phase 1: fuzz-test the migration path while it is still trivial. Author one quest and one item, save, then rename a definition ID and delete another; assert the loader reports the renamed ID as *migrated* and the deleted ID as an explicit **error with file and line** rather than a null. This is a unit test with no engine and no content volume.

**Fails if.** A renamed definition loads as a silent null, or a deleted definition aborts the load with no file and line. Either means the migration path is already broken while it is still trivial to fix.

**Status (M2b).** Validated. The definition-ID pass resolves renames and removals through `content/_aliases.yaml`, and an unmapped ID refuses the load with a blocker naming it. The committed historical fixtures load against their content pack in CI, so a rename without a map fails the build.

**Mitigation.** Renames ship only as migration-map entries plus an alias, never as bare edits; a CI check fails any diff that removes a definition ID without a matching migration entry; the `D-10` registry is the only place instance IDs are minted so the mismatch class is bounded; deleted definitions are retained as tombstoned aliases, never dropped.

### RK-04 — Quest expressiveness under a closed objective-type set

**Likelihood:** **Medium–High** — the charter's quest shapes (crafting, construction, faction state, relationships, puzzles, hidden, timed, world-state) are genuinely diverse, and this class of predicate framework usually leaks on the first three real quests.  
**Impact:** **High** — epic multi-stage quests are the charter's defining content; if they need hand code, `D-12`'s region-by-region plan cannot scale and the promise of story-driven artifact rewards degrades.  
**Owner:** Lead Systems Designer

**Risk.** **Quest expressiveness fails: the closed objective-type set (`D-07`) cannot describe real quests, so per-quest engine code reappears.**

**Why it matters.** `D-07` chose declarative objectives evaluated as predicates over world state precisely so that adding "deliver 3 crafted silver ingots to a specific NPC at night" is content, not engineering. `D-07` names its own failure signal: if the workaround for a real quest is engine code, the abstraction is wrong. Every such workaround is a permanent tax — `D-07`'s stated consequence is that these systems become unreadable and undebuggable if unconstrained, and the charter's artifact quests run for dozens of hours and cannot be tested by hand.

**Earliest inexpensive validation.** Phase 1, before any quest engine code is written: transcribe the charter's own example artifact chain as **pure YAML** against the proposed objective types and attempt to evaluate the graph with a throwaway evaluator. Success criterion is explicit — no new code per objective. This is a document-and-schema exercise costing hours, and it is decisive because it fails on paper instead of in month six.

**Fails if.** Any objective in the charter's own example chain requires hand-written evaluator code. Per `D-07`, that is the signal the abstraction is wrong, and the fix belongs in the objective schema rather than in a script.

**Mitigation.** Closed set of objective types, each with a strict schema (`D-07`); the quest **debugger** ("what is this quest waiting on right now") built as a Phase-1 deliverable, because `D-07` correctly identifies it as the only way to diagnose long artifact quests; a per-quest code workaround is treated as a design defect to be fixed in the schema, not absorbed into a script.

### RK-05 — Companion AI reliability and power budget

**Likelihood:** **High** — companion behaviour spans navigation, combat targeting, inventory, dialogue, morale, and the `D-06` tier handoff simultaneously, and is the single most commonly shipped-broken subsystem in RPGs of this shape.  
**Impact:** **Critical** — companions are load-bearing for solo-first design *and* for the RECRUIT loop; a failure here reduces the game to an emptier, lonelier version of the intended experience.  
**Owner:** Lead Gameplay Engineer

**Risk.** **Companion AI is unreliable or overpowered, so bringing companions is frustrating rather than helpful, or removes the fight entirely.**

**Why it matters.** The charter's solo-first pillar replaces the social role other players filled with NPC companions, and states the acceptance bar directly: *"Companion AI must be reliable enough that bringing companions feels helpful rather than frustrating."* Failure in either direction is fatal to a pillar, not a polish issue — a companion that blocks doorways, breaks pathing through player-built structures (`D-08`), or triggers friendly fire poisons every other loop it touches. The opposite failure is worse for progression: a cheap companion that solos content trivializes FIGHT and therefore PROGRESS (`D-09`).

**Earliest inexpensive validation.** Phase-1 automated soak, instrumentation before intelligence: one companion, one 30-minute scripted path through a town, a dungeon, and three fights, logging **stuck events, path-failure count, friendly-fire incidents, and time-to-kill ratio versus solo**. These metrics are cheap to collect and are the actual acceptance criteria; personality is not measured here and must not be built before these pass.

**Fails if.** Any of the four metrics is above the stated ceiling — most importantly the time-to-kill ratio, because a companion that shortens fights beyond the ceiling is a difficulty bypass, not a help. Personality work does not start until these pass.

**Mitigation.** Companion power budgeted below a same-level player on every axis so it accelerates rather than replaces play; explicit companion command set from the charter (follow/hold/defend/retreat/avoid combat) implemented as behaviour masks over one behaviour tree, never per-companion bespoke logic; the `D-06` tier rules forbid abstract tiers from advancing companion combat or inventory state, which prevents promotion-time desync.

### RK-06 — Building persistence and navigability

**Likelihood:** **Medium** — the piece model itself is low-risk because `D-08` deliberately avoided physics; the risk concentrates in navmesh rebuild on placement and in save-size behaviour at hundreds of pieces per cell.  
**Impact:** **High** — building is a top-tier retention loop and the charter's settlement fantasy depends on it; a home that half-loads or that companions cannot walk through fails a headline promise.  
**Owner:** Lead Gameplay Engineer

**Risk.** **Building persistence: player-placed structures fail to round-trip through sparse deltas, or become un-navigable for NPCs and companions.**

**Why it matters.** `D-08` chose socket/snap assembly specifically to guarantee two things — that structures remain **navigable** and **reliable to persist**, "a row in a table, not a physics resolution" — and both are prerequisites for companions and settlement NPCs to function inside them. Player-built structures are also the largest single writer of new baseline-divergent state, so they are where `D-05`'s delta design is most likely to break: hundreds of pieces placed in one cell, each a persisted entity with a `D-04` ULID.

**Earliest inexpensive validation.** Phase 1 or early Phase 2: place ~200 snapped pieces, place one companion and one NPC inside the structure, save, change a `content_version`-independent input, reload, and assert (a) piece count and transforms match, (b) the domain grid restamped per footprint change lets both actors path from the doorway to an interior socket (M7 reconciliation (2026-09-24)), (c) the delta save grows sub-linearly with piece count. All three are automatable headlessly and cost one afternoon.

**Fails if.** Any piece fails to round-trip, either actor cannot path to an interior socket after reload, or save size grows super-linearly with piece count. The navigation assertion is the one most likely to fail first and is the reason `D-08` chose snapping.

**Mitigation.** `D-08`'s socket model retained strictly — no free-form placement creep, because snapping is what makes navigability and persistence tractable; every piece is a registry-created (`D-10`) entity with a `D-04` instance ID and a health value applied by explicit rules; navigation updates as a domain grid restamped per footprint change (`D-13`); a piece-count ceiling per cell defined by the Phase-1 measurement and treated as a content constraint (`RK-08`).

### RK-07 — Tier-transition discontinuity

**Likelihood:** **Medium** — the mitigation is pre-specified and narrow, but the failure is observational: it shows up only under real player movement patterns, not in unit tests.  
**Impact:** **High** — this is the charter's "world that does not revolve around the player" pillar made visible. One teleporting merchant is enough to make the whole world read as fake, which retroactively invalidates the abstraction the project depends on for performance.  
**Owner:** Lead Gameplay Engineer

**Risk.** **Tier transitions (`D-06`) produce visible discontinuity — an NPC appears where its schedule could not have put it, or arrives with impossible state.**

**Why it matters.** `D-06` states that the hard part is tier transitions and that this is where such systems usually break. The charter requires NPCs to "appear to have lives" (sleep, work, travel, visit) while forbidding full simulation at distance, so the *presentation* of continuity is the entire deliverable. `D-06` already forbids abstract tiers from advancing combat or inventory, which removes the worst corruption class but leaves position, schedule, and liveness.

**Earliest inexpensive validation.** Phase 1: a scripted walk-past test. Place a scheduled NPC, record its position, move the player ~500 m away and back over a simulated in-game hour at accelerated time, and assert the NPC arrives within schedule-plausible distance of where the schedule says it should be. Cheap, deterministic, and repeatable in CI; no art or dialogue required.

**Fails if.** The NPC arrives outside schedule-plausible distance, or the promotion log shows position deltas that grow with simulated time — the latter indicates the abstract tier is drifting rather than being reconciled.

**Mitigation.** Abstract tiers advance **schedules and coarse position only** — never combat or inventory (`D-06`); on promotion an NPC is **reconciled to a legal state** rather than having its abstract state naively adopted; tier-boundary telemetry that logs every promotion with the delta between predicted and actual position, so the drift is a number we watch rather than a bug we discover.

### RK-08 — Content-scale authoring throughput

**Likelihood:** **High** — YAML schemas are verbose, and without linting, schema dump, and ID cross-reference tooling, authoring cost per item stays high enough that the content target is never reached.  
**Impact:** **High** — insufficient content is the quietest way this project fails: the systems all work, the vertical slice is fine, and the shipped world is thin. It is also the risk most likely to be under-reported because it produces no error and no crash.  
**Owner:** Lead Systems Designer

**Risk.** **Content scale: the pipeline cannot author thousands of items, hundreds of creatures, and hundreds of quests at quality, so the world ships empty or the systems get hand-coded per asset.**

**Why it matters.** The charter's DATA-DRIVEN DESIGN section mandates that content scale without rewriting gameplay code, and its content targets are large. `D-03` put content in YAML precisely so AI sessions and humans can author it with rationale inline, but `D-03` also makes the honest admission that this "requires content *tooling* to be pleasant or hand-authoring becomes miserable." The risk is not the format; it is throughput and consistency.

**Earliest inexpensive validation.** Phase 1: author 20 entries of one category (weapons) and measure minutes-per-entry. Then hand the schema — and *nothing else* — to a second session or a clean reader and count how many entries validate on first attempt. Both numbers are cheap and decide whether tooling is a Phase-1 deliverable or a Phase-3 luxury.

**Fails if.** Minutes-per-entry is high enough that the charter's content targets are unreachable within any plausible budget, or the clean-reader first-attempt validation rate is low. Either result promotes content tooling to a Phase-1 deliverable regardless of what else Phase 1 was going to contain — this is one of the few findings permitted to change Phase-1 scope, and `RK-10` records the cost of doing so.

**Mitigation.** `D-03`'s validator as a Phase-1 deliverable, not a later task, so a typo becomes a load-time error with file and line; minimal tooling scoped now (lint, schema dump, ID cross-reference check) per `D-03`; handcrafted content reserved for the charter's intentional list (major settlements, important dungeons, major quests, story locations) with everything else parameterized; compiled content cache for shipped builds to keep startup off the measured budget (`D-03`).

### RK-09 — Silent loss of the server-shaped architecture

**Likelihood:** **Medium** — the boundary is structurally enforced (separate `.csproj` with no Godot reference per `D-02`), which blocks whole classes of violation, but the remaining violations are logical, not structural: writing state from a view, or keying anything off "the player" as a global.  
**Impact:** **High** — if this fails, the cost lands on the one milestone the charter explicitly refuses to build now, and the honest remedy is a rewrite of every gameplay system the charter warns about.  
**Owner:** Technical Director

**Risk.** **The architecture silently stops being server-shaped: a local-player assumption or a direct state write makes the future co-op path impossible, contradicting `D-02` and `D-11`.**

**Why it matters.** The charter's tie-break rule (`D-12`) says to choose the working single-player implementation while leaving extension points, and `D-12` names exactly four we preserve: command-only mutation, stable identities, sparse versioned persistence, tiered simulation. An extension point that is never exercised is not preserved — it is *assumed*. `D-11` calls violations bugs, but a violation that compiles and passes every gameplay test is invisible until networking is scheduled, which is the worst possible time to find it.

**Earliest inexpensive validation.** Phase 1: two checks costing almost nothing. (1) An architecture test that fails the build if `src/Presentation/` mutates domain state rather than submitting commands — mechanically checkable. (2) A headless test that instantiates **two** player-ish contexts against one domain and asserts their inventories, XP, and quest states stay disjoint. No networking code is written for either.

**Fails if.** The architecture test cannot detect a deliberately-inserted violation when the violation is added as a control, or two player-ish contexts share any state. The first failure means the guarantee is decorative; establish the test before there is anything to violate, so violations never accumulate.

**Mitigation.** Structural separation enforced by project files so a violation fails to compile (`D-02`); presentation reads domain events and never owns truth (`D-11`); the four `D-12` extension points restated as a review checklist on every system added after Phase 1; no speculative networking abstraction is built to satisfy this risk — the checks are the guarantee.

### RK-10 — Project scope

**Likelihood:** **High** — this is the default outcome for projects of this stated ambition; nothing about our approach changes the base rate, only the discipline applied against it.  
**Impact:** **Critical** — it does not damage one system, it prevents the charter's actual goal ("If planning is sufficient, BUILD"). Everything else on this register becomes academic if no playable build ever ships.  
**Owner:** Technical Director

**Risk.** **Scope: the surface area is too large for a small AI-assisted effort, so the project produces architecture instead of a game.**

**Why it matters.** The charter asks for combat, magic, races, professions, building, settlement, base defense, companions, factions, economy, world events, dungeons, epic quests, and endgame, across an open world, and `PHASE_0.md` opens by forbidding implementation. `D-12` is the countermeasure written as a citable decision, but a decision is only binding if something enforces it: the risk is not that we forget the rule, it is that each system individually looks affordable. The concrete symptom is a Phase-3 codebase that still cannot be played for an hour.

**Earliest inexpensive validation.** Phase 1 exit criterion, stated before Phase 1 starts: a build in which a new player can move, fight, loot, level, craft one recipe, finish one quest, and reload a save — nothing else. Phase 1 is **failed** if any system not on `PROTOTYPE.md`'s minimum list was built, regardless of how well it was built. That is a scope measurement, not a quality judgement, and it is free.

**Fails if.** At the Phase-1 exit review, any working system exists that is outside `PROTOTYPE.md`'s minimum list. This is a scope measurement and not a quality judgement: the system may be excellent and Phase 1 still fails. `PROTOTYPE.md` independently names the same hazard from the prototype side.

**Mitigation.** `D-12` adopted verbatim and cited against scope creep; one region built completely before a second is designed; the charter's own preference rules enforced at review ("working combat with three weapons" over architecture for 500); every deferral recorded on the Phase table in `GAMEPLAY_LOOPS.md` with the phase it belongs to, so deferring is a visible decision rather than forgetting; `D-10`'s registry guarded against the god-object drift `D-10` itself warns about.

### RK-11 — Dirty-flag completeness

**Likelihood:** **Medium–High** — the flags are set in one place by design, but "one place" is a convention that holds only while every mutation path is remembered; the first direct state write from anywhere else defeats it silently.  
**Impact:** **Critical** — the save loads cleanly, passes integrity checks, and has simply forgotten a change. There is no crash and no error, so it surfaces as player-reported "my progress vanished" long after the cause.  
**Owner:** Technical Director

**Risk.** **A world change is applied but its cell is never marked dirty, so the change is absent from the sparse delta and is lost on the next load — while the save still validates as correct.**

**Why it matters.** `D-05` persists a cell only when it diverges from its deterministic baseline, and divergence is tracked by per-cell dirty bits with enumerated reasons (`nodes`, `spawns`, `flags`, `entities`, `buildings`, `terrain` — `WORLD_ARCHITECTURE.md` §5, `PERSISTENCE.md` `RK-P02`). That design is the whole reason unchanged world costs zero bytes, but it converts "I forgot to set a flag" from a cosmetic bug into **silent, permanent data loss**. Every other risk on this register announces itself; this one does not. It is also the one risk whose mitigation is purely structural — a single mutation dispatcher owning the flag — which means the mitigation itself is unproven until something tries to bypass it.

**Earliest inexpensive validation.** Phase 1, headless, no engine: a round-trip test per mutation class. Mine a node, kill a creature, open a container, place a building piece, change a world flag — save, reload, and assert field-by-field that each change survived. Then add the adversarial half: a test that **deliberately writes state while bypassing the dispatcher** and asserts the round-trip **fails**, proving the test can actually detect the bug rather than merely passing on correct code. That negative test is the real validation; without it we have only demonstrated that the happy path works.

**Fails if.** Any mutation class does not survive save/load, **or** the bypass test passes — meaning the harness is blind to exactly the failure it exists to catch.

**Mitigation.** One mutation dispatcher is the sole writer of dirty bits; no system sets flags directly. The flag taxonomy is enumerated rather than free-form, so a new mutation type must consciously choose a reason. The negative bypass test runs in CI so the guarantee is re-proven on every commit, not established once. Serialization asserts dirty-set consistency at save time and logs the enumerated reasons actually written, so a suspiciously small delta is visible in the save tool rather than invisible.

**Cross-reference.** Detailed rows: `PERSISTENCE.md` `RK-P02`; mechanism: `WORLD_ARCHITECTURE.md` §5.

### RK-12 — Offline catch-up across the save boundary

**Likelihood:** **High** — every load is an offline catch-up, and a long absence produces a very large `game_tick` delta that must be resolved without hitches or absurd outcomes.  
**Impact:** **Medium–High** — bounded wrongness is tolerable, but unbounded abstract outcomes are not: a wiped spawn population or a flooded economy both read as a broken world.  
**Owner:** Lead Systems Engineer

**Risk.** **World time advances across a save boundary; abstract tiers (C/D) must converge to a legal state for an arbitrary `tick_delta`, and the policy for a long absence is undecided.**

**Why it matters.** `D-06` gives every actor a tier and runs a single global tick, and `D-05` means a save is written at one `game_tick` and read at another — or, after a week away, at a much later one. Anything in an abstract tier that is not a pure function of `(state, tick)` diverges. `PERSISTENCE.md` `RK-P05` states the coupling constraint; `WORLD_ARCHITECTURE.md` `RK-A4` names the visible failure (a settlement that has "produced" for 300 days, plus a load hitch from resolving it all at once). This is the one promoted risk that needs a **design decision** rather than only an implementation, which is why it belongs in the project register rather than in a document-local table.

**Earliest inexpensive validation.** Phase 1, headless: take a saved world state, apply a `tick_delta` representing 1, 10, and 300 in-game days, and assert (a) each run completes within a fixed time budget, and (b) every resulting value lies inside its authored `[min, max]` band, and (c) running 300 days in one step equals running 300 single-day steps. The third assertion is the strong one: it separates genuine convergence from a policy that merely clamps the outcome.

**Fails if.** A clamped or saturated value falls outside its band, the load exceeds its time budget, or the one-step and N-step results disagree — the last of which means abstract simulation is order-dependent and cannot be trusted across a save.

**Mitigation.** Abstract tiers advance schedules, coarse position, and bounded economy counters only, and only as pure functions of `(state, tick)` (`D-06`, `PERSISTENCE.md` §5). Catch-up is applied in bounded chunks with a per-load cap, and the cap is a *policy* to be decided by the owner rather than discovered by a player. Per-cell and per-population values are clamped to authored `[min, max]`, and the clamp is logged so silent saturation is visible.

**Cross-reference.** Detailed rows: `PERSISTENCE.md` `RK-P05`, `WORLD_ARCHITECTURE.md` `RK-A4`. **Open decision:** the "while you were away" policy needs an owner call — see `DECISIONS.md` open questions.

### RK-13 — Atomic save commit on the target OS

**Likelihood:** **Medium** — the three-step staging-then-rename commit is written in POSIX terms, and `PERSISTENCE.md` `RK-P06` records it as untested on Windows, which is the stated baseline platform.  
**Impact:** **High** — this is the mechanism behind the corruption-recovery guarantee. If the commit is not atomic on the real filesystem, a crash mid-save can destroy the previous good save rather than preserving it.  
**Owner:** Technical Director

**Risk.** **The save commit's atomicity guarantee is assumed from POSIX semantics and is unverified on the actual target OS, where rename-over-directory behaves differently.**

**Why it matters.** `D-05` requires atomic writes with at least one rolling backup specifically so that corruption recovery is possible, and `PERSISTENCE.md` §7 builds that on a staging-directory → rename → verify sequence. Windows does not implement POSIX rename-over-directory semantics, so the guarantee as written may not hold on the baseline platform — and its failure mode is the worst possible one: the *previous* good save is destroyed while the new one is incomplete. This is worth promoting because it is cheap to falsify and catastrophic to discover late.

**Earliest inexpensive validation.** Phase 1, no engine: a process-kill test on the target OS. Begin a save, terminate the process at a randomized point mid-commit, relaunch, and assert that a valid save (either the old or the new, never neither) loads. Repeat across many iterations to catch the narrow window. This is a few hours of work and it either passes or it invalidates the save format's central guarantee.

**Fails if.** Any iteration leaves no loadable save, or leaves a save that validates but is a partial write, or the backup slot is unusable after the interrupted commit.

**Mitigation.** Write to a staging path and commit via the platform's atomic primitive, verified empirically rather than assumed; keep the previous save intact until the new one has been verified and fsynced; retain a rolling backup that is only ever replaced after a verified-good commit; run the process-kill test in CI on the target OS. `PERSISTENCE.md` §7's integrity table and quarantine path are the fallback if atomicity cannot be achieved, but a quarantine is a degraded guarantee and should not be the primary plan.

**Status (M2, M2b).** Validated on Windows. A real process kill at each of the six commit steps, for both saves and migrations, always leaves the old or the new complete save once the boot sweep has run; each test proves the kill landed at the intended step. Open: interference from real OneDrive/Dropbox sync engines (the save root is checked for them and the game can warn).

**Cross-reference.** Detailed row: `PERSISTENCE.md` `RK-P06`; mechanism: `PERSISTENCE.md` §7.

### RK-14 — Navmesh stitching at cell seams under player buildings

**Likelihood:** **Medium** — `D-08` guarantees pieces snap, but says nothing about the navmesh across a cell boundary; `WORLD_ARCHITECTURE.md` `RK-A2` records the case as explicitly unsolved.  
**Impact:** **High** — a broken navmesh does not announce itself; companions and settlement NPCs simply path wrong or stall, which is exactly the "frustrating rather than helpful" failure the charter singles out.  
**Owner:** Lead Gameplay Engineer

**Risk.** **A player-built structure straddling a cell boundary produces a navmesh seam that does not stitch, so companions and settlement NPCs cannot path through the player's own home.**

**Why it matters.** `D-08` chose socket/snap assembly specifically because it guarantees navigability — "a row in a table, not a physics resolution" — and navigability is what makes companions and settlement NPCs work inside player structures. Cell-based streaming (`WORLD_ARCHITECTURE.md` §11) rebuilds navmesh per cell, and a building that crosses a seam is the case where two independently built navmeshes must agree. `WORLD_ARCHITECTURE.md` `RK-A2` records this as **not solved**, and both `RK-06` (building navigability) and `RK-05` (companion reliability) are gated on it. It is promoted here because it is currently invisible in the project register: a reader consulting only `RISK_REGISTER.md` would conclude building navigability is covered when its hardest case is open.

**Earliest inexpensive validation (M7 reconciliation (2026-09-24)).** M7, headless plus a runtime recording: navigation is a domain grid (`D-13`), so the straddling case is a domain test, not an engine test. Place a snapping structure across a cell boundary, then ask a companion and a working NPC to path from one side to the other. Assert a complete path exists and is traversable, then save, reload and repeat; the grid is rebuilt from authoritative state and must be byte-equal. The cheapest useful version is a single straddling wall with a doorway — the smallest structure that spans a seam and requires routing through it. Likelihood stays as rated until M7's closeout records the evidence.

**Fails if.** No path is found, the path exits and re-enters the structure, or the path is valid before a cell reload and invalid after — the last outcome meaning navmesh state is not surviving streaming, which is worse than a seam bug.

**Mitigation (M7 reconciliation (2026-09-24)).** The domain grid (`D-13`) uses global node indices stored one tile per cell, so a seam is not a representation boundary and there is nothing to stitch; a footprint change restamps every tile it touches, neighbours included; the straddling case is in M7's test set and its runtime proof rather than discovered in playtest.

**Cross-reference.** Detailed row: `WORLD_ARCHITECTURE.md` `RK-A2`; gates: `RK-05`, `RK-06`.

### RK-15 — Cross-slice mutation inside the domain

**Likelihood:** **Medium–High** — the presentation→domain seam is enforced by a compile error, but the interior `Domain`→`Domain` boundary is not, and crossing it is easier and looks locally correct.  
**Impact:** **High** — `ARCHITECTURE.md` names this "the most common way authority boundaries rot", and the cost lands on the networking milestone `D-12` refuses to build now.  
**Owner:** Technical Director

**Risk.** **A domain system writes another system's owned component instead of submitting a command, and nothing detects it.**

**Why it matters.** `ARCHITECTURE.md` argues that the architectural boundary must be a compile error rather than a convention ("conventions erode and compile errors do not"), and then applies that argument **only to the presentation seam**. The rule that "keeps this honest" — a system may mutate only its own slice — was, as originally drafted, a sentence in a document with no check behind it. `RK-09` covers the presentation→domain hazard, which the `.csproj` boundary and the static-analysis check already catch. The intra-domain version is invisible to both.

An adversarial review found this by reading the interfaces: `IWorldState` was handed whole to every system with an unguarded `Write<T>(id, value)`, and "callable only from systems inside a tick drain" is not a property any type system, analyser or test can observe.

**Earliest inexpensive validation.** Phase 1 / Milestone 1–2, before there is anything to violate, so violations never accumulate: (a) an architecture test asserting `IWorldStateWriter` is not visible outside the `Domain` assembly (a compile-failure fixture, not a runtime assertion); (b) a composition-time check that every registered component key is claimed by exactly one system, with a deliberately-unclaimed key asserted to fail startup; (c) the two-world-instance disjointness test (two stores, two registries, no shared mutable state) — which catches static and singleton state the moment it appears.

**Fails if.** A type outside `Domain` can call a write operation; an unclaimed component key boots successfully; or two world instances share any mutable state.

**Mitigation.** The store's write surface is `internal` to `Domain` (`IWorldStateWriter`), so `Application` and `Presentation` cannot reach it at all — the same class of guarantee as the Godot-reference ban. Each system's owned slice is an explicit typed component key registered at composition, making "a component nobody owns" a startup error rather than a silent success. Both checks are scheduled in Milestone 1–2 rather than retrofitted.

**Residual, stated honestly.** A `Domain` system deliberately writing another `Domain` system's *component* is still only detectable by review. This entry exists so that residual is a recorded, owned risk rather than an unstated assumption.

### RK-16 — Read-path correctness has no owner

**Likelihood:** **High** — three documents specified three load orders and they were not reconciled until this review; only the *write* sequence was ever written as "follow exactly".  
**Impact:** **High** — the failure is silent, appears only with a save old enough to migrate, and produces stale derived values and dangling references that look like content bugs.  
**Owner:** Technical Director

**Risk.** **Baseline regeneration, delta application, migration, derived-cache recomputation and alias resolution are specified in incompatible orders across documents, and no document owns the read path as a whole.**

**Why it matters.** Every other persistence risk in this register concerns what is **written** (`RK-01` determinism, `RK-11` dirty flags, `RK-13` atomic commit, `RK-06` building volume) or time passing (`RK-12`). None covered **reading**. The specific defects found: `ARCHITECTURE.md` applied "manifest + player state + sparse delta" *before* streaming had regenerated the cell baselines the deltas are relative to — the exact inversion of what `SYSTEMS.md` and `PERSISTENCE.md` describe; `SYSTEMS.md` said a corrupt save "falls back to a backup slot rather than partially loading" while `PERSISTENCE.md` sanctioned partial load as "partial recovery beats none"; and `PERSISTENCE.md` required derived caches to be recomputed only *after* the whole migration and alias pass had run, with no numbered sequence expressing it. The worst available reading is also the most natural one — deserialise section, recompute its caches, validate — and it fails quietly.

**Earliest inexpensive validation.** Phase 1, headless, no engine: a **fixture save at an old `schema_version`** carrying (a) a renamed definition ID resolvable only through the alias map, (b) a persisted derived value that must be recomputed, and (c) a deliberately corrupt section. Load it and assert: derived values are recomputed *after* alias resolution and migration; the quarantined-section path produces a complete, reference-valid world; and a `save_format` mismatch refuses rather than guessing. `PERSISTENCE.md` §10's test list already covers pieces of this — the gap is a test that exercises the **order**, not the individual steps.

**Fails if.** Any derived value survives a load stale; any dangling reference results from a resolvable alias; or a partially-loaded world is emitted as valid with unresolved references.

**Mitigation.** One numbered, normative load sequence published in `PERSISTENCE.md` alongside its write sequence, and `ARCHITECTURE.md` §8 corrected to cite it rather than restate it (done in this review). Partial-load policy decided once, in one document. Derived caches recomputed at a single explicit point after migration and alias resolution, asserted by the fixture test above. **Now enforced by `PERSISTENCE.md` invariant `I-6` and required test `T-27`** (Phase 1), which asserts the *sequence* rather than its individual steps. A subsequent adversarial pass made the fair point that a written sequence with no test behind it is a convention, and conventions erode — `I-6`/`T-27` are the answer, and they are scheduled in Phase 1 rather than deferred.

## Promoted from the document-local risk tables

`PERSISTENCE.md` keeps risks `RK-P01`..`RK-P13` and `WORLD_ARCHITECTURE.md` keeps `RK-A1`..`RK-A9`, each local to their document and each naming the corresponding project ID where one exists. Both schemes were chosen to avoid colliding with `RK-01`..`RK-10`, and both cross-reference *back* to this register. The direction was previously one-way: this register did not mention `RK-P*` or `RK-A*` at all, so a session consulting only the register would have missed the risks below. **`RK-P09`..`RK-P13` were added during the final reconciliation pass** (`PERSISTENCE.md` §11.1) when the persistence specification was restored from a truncated revision.

The four promoted to `RK-11`..`RK-14` were those with **no** project-register equivalent. The remainder were deliberately **not** promoted, and are recorded here so the decision is visible rather than looking like an omission:

| Local ID | Maps to | Why not promoted |
|---|---|---|
| `RK-P01`, `RK-A3` | `RK-01` | Already the same risk, stated from the persistence and world sides |
| `RK-P03` | `RK-03` | Already covered by definition-ID-as-public-API |
| `RK-P04` | `RK-06` | Already covered by building persistence and save size |
| `RK-A1` | `RK-07` | Already covered by tier-transition discontinuity |
| `RK-P08` | — | Quarantine UX quality; a presentation concern, not architectural |
| `RK-P07` | — | Migration-chain testability; real, but bounded by `RK-03`'s migration mechanism |
| `RK-A5` | — | Accepted design simplification ("frozen dungeon" reading as a bug); a design risk we are taking knowingly |
| `RK-A6` | — | Cell-size choice; a tuning constant with a documented migration path (`PERSISTENCE.md` §9) |
| `RK-A7` | — | Population-drift tuning; a parameter, not an architectural risk |
| `RK-A8` | `RK-15` | **Superseded:** the "diverged-actor rule" this deferred to was undefined; it is now the slot-key merge rule (`PERSISTENCE.md` §5.3, `I-8`), and the residual is cross-slice ownership erosion |
| `RK-A9` | — | Weather/world-event scope; capped by design at ≤ 8 region events |
| `RK-P09` | — | **Addressed in place:** delta growth is now bounded by the `Rebase` primitive (`PERSISTENCE.md` §5.6, `I-7`). Retained as a local risk because the bound is a mechanism, not a proof |
| `RK-P10` | `RK-15`–adjacent | **Addressed in place:** entity/baseline identity collision fixed by the slot key and merge-on-load (`PERSISTENCE.md` §5.3). Not promoted separately because it is no longer open |
| `RK-P11` | — | **Addressed in place:** RNG draw-order sensitivity removed by keyed derivation and deleting the global counter (`PERSISTENCE.md` §1.1 `I-9`) |
| `RK-P12` | `RK-16`–adjacent | **Addressed in place:** content *tuning* is baseline-locked and requires migration (`PERSISTENCE.md` §5.5). Adjacent to the load-path risk but distinct from it |
| `RK-P13` | — | **Open:** scoped saves are a mandatory, unbudgeted side effect; cadence-capped in `PERSISTENCE.md` §8.1 but the correctness-versus-budget arbiter is stated, not measured |

**Instruction to a future session:** if any of the mapping rows above changes — in particular if `RK-P07`'s migration chain or the slot-key merge rule (`RK-P10`) is materially altered — re-evaluate promotion. The register is the authority; the local tables are detail. `RK-P09`–`RK-P12` were opened and addressed *within* `PERSISTENCE.md` during the final reconciliation pass; they are listed here so a reader consulting only the register does not mistake "not promoted" for "not known".

## Top three risks requiring owner awareness

These are the three where a project-owner decision or owner-funded time matters, not just engineering discipline.

**1. `RK-10` — Scope, and the Phase-1 exit criterion that enforces it.**
This is first because it is the only risk that can make every other mitigation irrelevant. The owner should be aware that Phase 1 is deliberately defined as *smaller* than `PHASE_0.md` STEP 15's full list where the two differ in emphasis, and that the exit criterion includes a negative test: building anything outside `PROTOTYPE.md`'s minimum counts as Phase 1 failure even if it works. If the owner's expectation is a broad prototype, that expectation should be corrected now rather than at Phase-2 review, because it changes what "done" means.

**2. `RK-02` — The engine budget measurement, and the one condition under which the engine decision legitimately reopens.**
`D-01` was settled on a clear rationale and `PHASE_0.md` forbids reopening it casually. What the owner should know is that `D-01` wrote its own revisit trigger and that trigger is a **measurement**, not an opinion: if the greybox 2×2 km stress scene misses the target frame budget after streaming and LOD work, we are obliged to report that number rather than compensate with art direction. The owner should expect a specific frame-time figure from Phase 1, and should treat a failure to produce that figure — not a failure to hit budget — as the alarming outcome.

**3. `RK-05` — Companion quality is a pillar-level acceptance risk, and its validation is instrumentation, not intelligence.**
The charter makes companions replace the social role other players held, and states the bar as "helpful rather than frustrating." The owner should be aware that Phase 1 deliberately measures companions **quantitatively** (stuck events, path failures, friendly-fire incidents, time-to-kill ratio versus solo) and deliberately does **not** invest in companion personality, dialogue, or personal quests before those numbers pass. If the owner's mental model of Phase 1 includes a memorable companion, that is Phase 2 work and should be scheduled as such — personality built on unreliable pathing is wasted effort.

**No other owner input is required to proceed.** `RK-01`, `RK-03`, `RK-04`, `RK-06`, `RK-07`, `RK-08`, and `RK-09` are engineering-owned and have Phase-1 validations specified above.

## Risks we are deliberately accepting

These are live risks we have decided **not** to mitigate further right now, with the reason and the trigger that would change our mind. If a future session finds one of these, the correct response is to cite this section — not to "fix" it.

| Accepted risk | Why we accept it |
|---|---|
| **Lower visual ceiling than UE5 (`D-01`).** | `D-01` made this trade knowingly with the reasoning that a tiny AI-assisted team will not out-art a studio at any engine, so the marginal value of UE5's renderer is lower than it appears. Compensated by art direction, not fidelity. Reopens only if the art direction changes to require fidelity Godot demonstrably cannot reach. |
| **Missing open-world infrastructure: no World Partition equivalent, no built-in crowd AI (`D-01`).** | We accept writing streaming, LOD, crowd abstraction, and layered simulation ourselves, because the same `D-01` rationale (diffable text formats, headless testability, no licensing exposure) is worth more to an AI-assisted workflow than the built-ins. Budgeted as `WORLD_ARCHITECTURE.md` work, not as a surprise. |
| **Command/event indirection cost and its Phase-1 overhead feeling (`D-02`).** | `D-02` states plainly that a trivial "player picks up item" flow will feel like unnecessary overhead during Phase 1 and **must not be abandoned because of that feeling**. We accept the friction as the price of headless testability and the networking seam. Reopens only if profiling shows a material frame-time cost — and even then the fix is batching the bus, not dissolving the boundary. |
| **The friction cost of defining content in YAML at volume (`D-03`, `RK-08`).** | Accepted in exchange for inline comments (design rationale next to content), clean diffs, and generatability by AI sessions. We accept that this requires tooling investment and a compiled cache, and we accept YAML's verbosity as a real cost rather than pretending it is free. |
| **Save bloat from ~26-character ULID instance IDs at scale (`D-04`).** | Accepted because sortability, legibility in a save dump, and the lack of a central counter are worth more than bytes, and because `D-05`'s sparse deltas mean the unchanged world costs nothing. `D-04` names the contained fallback (save-local integer table with ULIDs retained as the authoring key) if size becomes a measured problem. |
| **Some future multiplayer work will be genuinely harder than if we designed for it now (`D-12`).** | This is the accepted trade of the entire project. `D-12` preserves four named extension points and `RK-09` validates them cheaply; beyond that we build no speculative MMO infrastructure, because the charter states that would destroy scope. Reopens only if multiplayer becomes a *scheduled milestone* rather than a possibility. |
| **Building is one storey in v1 (`D-14`, M7 reconciliation (2026-09-24)).** | Accepted because bodies stand only on terrain and navigation is a 2-D domain grid; walkable elevated surfaces would change the movement function every mover shares and would need 3-D navigation. The answer to pressure for lofts or stairs stays "more pieces, not physics" until an owner ruling reopens vertical building. |
| **Player freedom deferred: no voxel or free-form building; players cannot author arbitrary shapes (`D-08`).** | Accepted because snapping is what guarantees navigability and reliable persistence, both of which are hard prerequisites for companions and settlement NPCs. The response to player-expression limits is a better piece catalogue, never a physics engine or a looser structural model. |
| **Some visual discontinuity and pop at tier boundaries and streaming seams (`D-06`).** | Accepted as a bounded presentation cost in exchange for not simulating every actor at full fidelity, which the charter forbids. `RK-07` bounds the worst symptom (NPC teleporting against its schedule) rather than eliminating all seam visibility. |
| **Content that is thin in the first shipped region relative to the eventual target numbers.** | Accepted by design: `D-12` and the charter both require one region done completely before a second is designed, and `PHASE_0.md` forbids generating hundreds of items, monsters, or quests now. We are choosing depth of one region over breadth of an empty world. |
| **Grinding remains intentional and useful (charter §11).** | Accepted, not a risk to eliminate: the charter explicitly declines to remove grinding and instead asks that it rarely be mandatory busywork. The economy guards below target *degenerate* farming, not the legitimate "I'm going hunting" session — see the `GAMEPLAY_LOOPS.md` economy section for the distinction. |

## READY FOR IMPLEMENTATION

Consolidated summary required by `PHASE_0.md` FINAL REVIEW. This section is the entry point for the next session.

### Chosen engine

**Godot 4.x with C# as the primary gameplay language** (`D-01`). GDScript is permitted for editor tooling only. The engine choice is settled and is **not** reopened by preference; the only sanctioned reopen triggers are the three written in `D-01`, of which the relevant one is the measured frame-budget failure tracked as `RK-02`. Visual ceiling is accepted as lower than UE5 by deliberate trade.

### Core architectural pattern

**Single-player, server-shaped domain authority (`D-02`), with presentation as a pure observer (`D-11`).**

- All authoritative gameplay state lives in **engine-agnostic C# domain assemblies** under `src/Domain/`. Only `src/Presentation/` may reference Godot types. The boundary is enforced by separate `.csproj` files with no Godot reference, so a violation fails to compile rather than being merely discouraged.
- All mutation flows through a **command/event bus**. The presentation layer submits commands and subscribes to domain events; it never writes state and never owns truth. Presentation-side prediction is for feel only and is never permitted to write state (`D-11`).
- **Content is YAML data** under `content/`, validated at load time against C# schemas, referenced by dotted string definition IDs (`D-03`).
- **Identity is dual-namespace** (`D-04`): stable dotted definition IDs (public API, renamed only via migration map) and ULID instance IDs minted by the **Entity Registry** (`D-10`), which stores no gameplay state and holds no game rules.
- **Persistence is sparse deltas over a deterministic baseline** (`D-05`): a small manifest (`PERSISTENCE.md` §4.2) plus fully serialized player/companion state plus only those entities and cells that differ from the deterministic baseline, each changed cell recording the baseline it was made against. Saves are written atomically (staging + rename + verify) with two backup generations.
- **Simulation is tiered** (`D-06`): Tier A full, Tier B simplified regional, Tier C abstract schedule/economy, Tier D stored state only. Abstract tiers advance schedules and coarse position only, never combat or inventory.
- **Scope discipline** (`D-12`): one region completely before a second; no networking code, no dedicated server, no speculative MMO abstraction.

### First implementation milestone

**Milestone 1 — "the seam is real and the world is reproducible."**

A headless, engine-free C# solution that can: load content from YAML with validation errors carrying file and line; create a player entity through the registry so it has a ULID from birth; generate a fixed 2×2 km region deterministically from a seed and reproduce a stable digest of it; award XP and apply a level-up through a command; place one item into the player's inventory and move it to a container; write a sparse-delta save and load it back to an identical domain state.

Explicitly **not** in Milestone 1: rendering, art, animation, dialogue, quests, combat feel, companions, building, or anything a player would call "the game." The milestone proves three things cheaply — that the domain layer is genuinely engine-free and testable, that determinism holds well enough for `RK-01`'s save format, and that the command/event seam is real rather than aspirational. Its exit artifact is a green test run, not a screenshot.

### First 10 development tasks, in dependency order

Ordered so that no task depends on a later one, and written as the **critical-path slice** that must precede any Phase-1 feature work. The first three are the cheap spikes for `RK-01`, `RK-09`, and `RK-10`; do not reorder them behind feature work.

**Relationship to `ROADMAP.md` (read this before executing).** `ROADMAP.md` §8 also publishes a first-ten-tasks list, and it is the **scheduling authority**: its milestone graph (`M0`..`M16`), its dependency table, and its critical path govern *when* work happens. This list is the risk-focused view of the same early sequence and is **not** an alternative plan. Each task below names its `ROADMAP.md` milestone equivalent so the two documents can be read together. **Where the two sequences differ, `ROADMAP.md` wins** — the differences are deliberate and are noted at the end of this list.

1. **Repository and build scaffolding** (`ROADMAP.md` `M0`). **Three projects**, per `ARCHITECTURE.md` §3 (the structure authority): `src/Domain/`, `src/Application/`, and `src/Presentation/` as separate projects; `Domain.csproj` and `Application.csproj` with **no Godot reference**; `Presentation` is the only project that may reference Godot. Tests live under top-level `tests/Domain.Tests/`, not inside `src/`. Run `dotnet test` in CI on an empty suite to prove the harness works. **Superseded:** an earlier revision of this list named only `src/Domain/` and `src/Presentation/` and put tests in `src/`, contradicting `ARCHITECTURE.md` §3.
2. **Headless determinism probe** — `RK-01` validation (`ROADMAP.md` `M2` risk spike). Fixed-seed generator digest test, plus the "trivial content change must be *detectable*" variant. Can be written against a stub generator before real generation exists; its job is to prove the *test* distinguishes stable from unstable, not to generate a pretty world.
3. **Architecture enforcement test** — `RK-09` validation (`ROADMAP.md` `M1`/`M2`). A test that fails if `src/Presentation/` references or mutates domain state, plus the two-player-context disjointness test. Establish this before there is anything to violate, so violations never accumulate.
4. **Identity and Entity Registry** (`D-04`, `D-10`; `ROADMAP.md` `M2`). ULID instance ID minting, dotted definition ID parsing, registry create/lookup/enumerate. Heavily tested: `D-10` states every system depends on it and that it must remain trivially correct.
5. **Content loading and validation** (`D-03`; `ROADMAP.md` `M1b`). YAML loader, C# schema definitions, and a validator that reports file and line. Seed with the handful of examples `PHASE_0.md` STEP 7 permits — not a content library. This is the prerequisite for everything content-shaped.
6. **Command/event bus and domain event dispatch** (`D-02`, `D-11`; `ROADMAP.md` `M1`). The seam plus the two-player-context test from task 3 made real. Resist optimizing it: `D-02` explicitly expects it to feel like overhead here.
7. **Deterministic world generation, fixed seed, fixed `content_version`** (`D-05`, `D-06`; `ROADMAP.md` `M2`). Wire task 2's probe to the real generator. Record seed and content version in the manifest. Freeze the generation interface before adding features to it.
8. **Sparse-delta save/load with atomic write and rolling backup** (`D-05`; `ROADMAP.md` `M2`, migration harness in `M2b`). Manifest, player/companion state, and per-cell delta versus baseline. Round-trip test asserting identical domain state after load. Includes the deliberately-shaped migration seam from `RK-03`, even while there is only one content version.
9. **Minimal player domain state and progression math** (`D-09`; `ROADMAP.md` `M2c`). Attributes, XP, level, one skill, one technique — the smallest set that lets a command produce a visible state change. Progression math is unit-testable outside the engine, which is the entire reason `D-02` exists; keep it there.
10. **First playable presentation shell** (`D-01`, `D-11`; `ROADMAP.md` `M3`). Godot full-body third-person movement with seamless first-person zoom that submits movement and interaction commands and renders events. No direct state writes. This is the point at which the seam stops being a test fixture and starts carrying a player, and it is the last item before Phase-1 feature work begins.

**Deliberate differences from `ROADMAP.md` §8**, recorded so a later session does not read them as an inconsistency:

- **Task 2 is first here, sixth there.** Both are correct: the probe is a *risk* priority (`RK-01` is the highest-impact risk) and a *scheduling* lower priority because a stub generator can satisfy it before real generation exists. `ROADMAP.md` `M2` therefore files it as a risk spike on the same path.
- **Task 3 here is folded into `M2` there.** The register pulls the architecture test forward because it costs almost nothing when there is nothing to violate and becomes expensive once violations exist. `ROADMAP.md` reaches the same guarantee via `M2`'s exit criteria.
- **`ROADMAP.md` §8 tasks 8 and 10 are not duplicated here.** The save-migration harness (`M2b`) is covered by this register as `RK-03`'s validation, and the telemetry skeleton belongs to `GAMEPLAY_LOOPS.md` §13, which needs per-activity rate measurement for `E-1`, `E-2`, and `E-4`. They are real tasks; they are simply owned by other documents.

Phase-1 feature work (combat, magic, loot, crafting, one quest, one companion, dialogue, one dungeon) begins **after** task 10 and is scoped by `PROTOTYPE.md`. The full Phase-1 definition, the vertical slice, and the dependency graph beyond this point belong to `ROADMAP.md` (`M3`..`M9`) and `PHASE_0.md` STEPs 15–17; they are not duplicated here.

### Unresolved decisions requiring the project owner's input

`DECISIONS.md` is the authority for open owner questions and now records four; the fourth was added as a direct consequence of promoting `RK-12` into this register. These four are the complete list. **No additional owner decision is required to begin implementation**, and none is manufactured here. Restated for completeness, each with the documented default that lets work proceed unanswered:

1. **Working title and setting temperature.** Architecture does not depend on this; content does, and Phase 1 needs *some* flavor text. **Default if unanswered:** a melancholic, ancient, low-magic-feeling high fantasy tuned for "the world is the primary character." Reversible and cheap while content volume is near zero.
2. **Target platform baseline.** Affects the streaming and LOD budget in `WORLD_ARCHITECTURE.md` and therefore the `RK-02` measurement's pass/fail threshold, and it is the platform `RK-13`'s atomic-commit test must run on. **Default if unanswered:** Windows desktop, 16 GB RAM, mid-range discrete GPU; not a VR or console target in Phase 0–2.
3. **Visual fidelity target.** **Default if unanswered:** readable and atmospheric in place of AAA fidelity, consistent with `D-01`.
4. **The "while you were away" policy** (raised by `RK-12`). When a save is loaded after a long absence, world time has advanced and abstract tiers must converge. The *mechanism* is specified (`D-06`); the *policy* is not: how much simulated time is honoured, whether it is capped, and whether the player is told what happened while they were gone. **Default if unanswered:** honour a bounded catch-up window, clamp all abstract values to their authored `[min, max]`, and never present a summarised "while you were away" report in Phase 1–2. This is reversible at any point before settlement simulation exists, so it does not block Milestone 1.

Decisions 1 and 3 are pure content and art direction and can remain unanswered through Milestone 1 without blocking anything. Decision 4 has no dependency before settlement simulation (Phase 2+) and can be deferred safely. **Decision 2 has a soft dependency:** the `RK-02` frame-budget spike produces a number regardless, but the number cannot be judged pass or fail without a baseline. Proceeding on the stated default (Windows desktop, 16 GB RAM, mid-range discrete GPU) is the correct reversible choice; if the owner intends a different baseline, saying so before task 10 avoids re-measuring the stress scene.

*Assumptions recorded by this document:* (a) the owner will run the `RK-02` budget check against the default platform baseline if no baseline is supplied; (b) all named roles (Technical Director, Lead Gameplay Engineer, Lead Systems Designer) currently resolve to one or two individuals, so the Owner field denotes accountability for reporting, not staffing; (c) `PROTOTYPE.md` and `ROADMAP.md` will define Phase 1 in more detail and this document's Milestone 1 is the subset that must precede all Phase-1 feature work; (d) where this register and `ROADMAP.md` give different orderings of the same early tasks, `ROADMAP.md` governs scheduling and the differences are itemised above; (e) `PROTOTYPE.md` owns the Phase-1 content minimums this register cites, and `GAMEPLAY_LOOPS.md` owns the loop-level phase table.
