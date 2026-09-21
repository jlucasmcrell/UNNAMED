# Adversarial Review — ARCHITECTURE / SIMULATION / SCOPE / AI-SESSION HAZARDS

**Scope.** `ARCHITECTURE.md`, `SYSTEMS.md`, `WORLD_ARCHITECTURE.md`, `DECISIONS.md`, `PERSISTENCE.md`, `DATA_MODEL.md`, `ROADMAP.md`, `PROTOTYPE.md`, `VERTICAL_SLICE.md`, `GAMEPLAY_LOOPS.md`, `RISK_REGISTER.md` vs `PROJECT_CHARTER.md` and `PHASE_0.md`.

**Exclusions.** This review does **not** restate `RK-01`..`RK-14`. Where one of those risks exists, the *mitigation* is attacked instead. It also does not duplicate two parallel adversarial reviews produced in the same pass: `docs/reviews/PERSISTENCE_ADVERSARIAL_REVIEW.md` (delta/baseline identity, dirty-vs-divergence, entity slot mapping, RNG model, Windows commit) and `docs/REVIEW_progression_scope_economy.md` (XP curve arithmetic, axis duplication, death-penalty conflict, currency faucet, slice scope). Findings here are the ones those two do not contain.

**Caveat on a moving target.** The document set was being edited while this review was written, and at least two line-level defects found during the pass were corrected in place before publication (a stale "assumption" in `SYSTEMS.md` §Assumptions that still claimed three sibling documents did not exist, and `S-21`'s "Reads" line, which cited four unrelated systems). Line numbers below should be treated as approximate for the MINOR findings and substantively verified for the CRITICAL and MAJOR ones, which were checked last. The *class* of defect they represent — a normative document drifting out of step with the set it defers to — is itself finding M-2, and is not fixed by correcting the instance.

Every criticism is grounded in a `file:line` quote. No fixes are proposed beyond a brief "what I would do instead".

---

## CRITICAL

### C-1 — The World State Store makes the "one system owns each slice" rule unenforceable, and the god-object audit is blind to the only violation that matters

**Evidence.**

> `SYSTEMS.md:70` — "**Events/interfaces.** `Read<T>(id)`, `Write<T>(id, value)` — callable **only** from systems inside a tick drain, never from presentation."

> `ARCHITECTURE.md:127` — `void Register(ICommandBus bus, IEventBus events, IWorldState world);`

> `ARCHITECTURE.md:224` — "3. **Ownership is declared and auditable** via the table in §5 and per-system file headers. A system that starts owning two unrelated concerns is a review failure."

> `ARCHITECTURE.md:53` — "This matters because conventions erode and compile errors do not: a developer (or an AI session) who tries to reach for `Node` or `Vector3` inside a domain system gets a build failure, not a code review comment. **This is the single most important structural rule in the project.**"

`IWorldState` is handed whole to every system's `Register`. `Write<T>(id, value)` takes a target and a value and nothing else — no owner token, no system identity, no provenance. The "callable only from systems inside a tick drain" restriction is a sentence in a document, not a boundary: a tick drain is not a callable-frame property that the type system, an analyser, or a test can see. `ARCHITECTURE.md:226` guards the *store* ("Any `if` statement inside it is a bug") but nothing guards *callers* of the store.

So the project has two protected invariants and one unprotected one. The seam (`D-02`/`D-11`) is genuinely structural: separate `.csproj`, verified by `PROTOTYPE.md:264` (C1, "CI: build + `grep`") and `PROTOTYPE.md:267` (C4, static analysis for assignments from `src/Presentation/**`). `D-10`'s narrowness is real. But the rule that the architecture itself names as the thing that "keeps this honest" —

> `ARCHITECTURE.md:212` — "A system may mutate only its own slice. Everything else goes through a command."

— and that `ARCHITECTURE.md:135` says "prevents two systems quietly owning the same field — **the most common way authority boundaries rot**", has no enforcement at all. Every check in `ARCHITECTURE.md` §10 and `PROTOTYPE.md` §7.1 tests the presentation→domain boundary and nothing else.

**Why it matters.** The unprotected boundary is the *interior* one, and it is the one an AI session will cross, because crossing it is easier and looks locally correct: `world.Write(statsId, newStats)` compiles, passes every listed test, produces a plausible diff, and is exactly the shortcut `ARCHITECTURE.md:214` says must not happen. `RK-09` covers this hazard only as "writing state from a view, or keying anything off 'the player' as a global" (`RISK_REGISTER.md:174`) — both of which the `.csproj` and static-analysis checks *do* catch. The intra-domain version is in no register entry and no test list, and it is precisely the erosion `D-10`'s own "Revisit if" clause forecasts (`DECISIONS.md:205`).

**What I would do instead.** Make the store's surface per-system: hand each system a narrowed view interface (`IIdentityView`, `IInventoryStore`, `IQuestStore`) rather than `IWorldState`, so cross-slice mutation fails to compile the same way a Godot reference does. If that is too much ceremony for Phase 1, at minimum add a `Write` overload carrying an owner id and assert it at write time, and make the ownership test part of the architecture-test milestone that `RISK_REGISTER.md:366` already schedules before there is anything to violate. Add it to the register as a risk distinct from `RK-09`.

---

### C-2 — Three documents specify three incompatible load orders and procedures, none of which reconciles baseline regeneration with delta application

**Evidence.** Three sequences, all stated as normative:

> `ARCHITECTURE.md:255` — "5. Register systems → explicit ordered list from the composition root / 6. **Load save (if any)** → apply manifest + player state + sparse delta, run migrations / **7. World streaming begins** → assign initial cell tiers around the player"

> `SYSTEMS.md:340` — "**Load order is:** validate manifest → validate content version → apply migrations → build registry baseline → apply world delta → deserialize player/companion state → emit one consolidated `WorldLoaded` event so views rebuild exactly once. Failures surface loudly; **corrupt saves fall back to a backup slot rather than partially loading**"

> `PERSISTENCE.md:402` — "| T-02 | Deterministic generation | Same `(seed, content_hash)` yields a byte-identical baseline across processes and 100 runs | **P1** |"

> `PERSISTENCE.md:335` — "| Quarantined section is `cells`/`entities`/`buildings` | Load **without** it, set `flags.quarantined_sections`, and state exactly what was lost … Partial recovery beats none |"

> `PERSISTENCE.md:272` — "// Values derived from 'might' are NOT recomputed here: they are caches, and the domain layer recomputes them after load."

Three problems compound:

1. **`ARCHITECTURE.md:256` is wrong in order and in substance for the format it describes.** It applies "manifest + player state + sparse delta" *before* streaming assigns cell tiers. But a delta is meaningful only against a regenerated baseline, and baselines are generated by streaming per cell (`WORLD_ARCHITECTURE.md:106`, "A cell is not a file. It is a **record** produced by joining authored data, procedural output, and persistent delta"). As written, step 6 applies deltas to cells that step 7 has not yet generated. `S-03` says so explicitly — "at load it is rebuilt by regenerating the baseline from `(seed, content_version)` and applying the delta" (`SYSTEMS.md:69`) — so `ARCHITECTURE.md` §8 and `SYSTEMS.md` §2 disagree about the load path, and `PERSISTENCE.md` §1.2 (line 28) agrees with `SYSTEMS.md`.

2. **`SYSTEMS.md:340` and `PERSISTENCE.md:335` disagree about whether partial load is legal.** "corrupt saves fall back to a backup slot rather than partially loading" is the direct negation of "Load **without** it … Partial recovery beats none". For a 1–2 person team there is no tie-breaker.

3. **`PERSISTENCE.md:272` states a rule that its own load path cannot express.** Derived caches must be recomputed only after the whole migration chain *and* the alias/tombstone pass have run (`PERSISTENCE.md:304`, "No hit at all → **hard content error**"). The obvious implementation — recompute per section as it deserialises — produces exactly the stale numbers the comment forbids. There is no numbered load sequence to contradict it.

**Why it matters.** `PERSISTENCE.md:8` states the reader assumption: "another AI coding session implementing persistence from this document alone". Given three sequences, the worst available reading is also the most natural one (deserialise section → recompute its caches → validate), and it produces stale derived state, false quarantines of data that a rename would have resolved, and dangling references on the partial-load path. This is a document defect that costs a rewrite of the load path, and it is invisible until a save is old enough to migrate.

**What I would do instead.** Publish one numbered load sequence in the same style and at the same authority as `PERSISTENCE.md:314` ("### 7.1 Write sequence (follow exactly)"), make it the only one, and fix or delete `ARCHITECTURE.md` §8 step 6. Decide partial-load vs backup-fallback once, in one document.

---

### C-3 — The determinism contract cannot hold in the form written, its enabler is not persisted, and the mechanism that would prove it does not exist until Phase 3

**Evidence.**

> `SYSTEMS.md:37` — "Determinism requirement (D-05): given `(seed, content_version, command_log)`, tick outcomes must be reproducible. Systems must not read wall-clock time, unseeded RNG, frame delta, or iteration order of unordered collections."

> `SYSTEMS.md:50` — "**Persistent.** None (the command log is written only by the save system when a diagnostic/debug save is requested)."

> `SYSTEMS.md:32` — "Drain the queue in a **deterministic system order** — the order systems are listed in §2 is the required order; ties broken by ascending instance ID."

> `ARCHITECTURE.md:134` — "Systems are registered in an explicit, ordered composition root. Order is data, not implicit discovery."

> `PROTOTYPE.md:371` — "6. A single recorded (video or log) **10-minute scripted playthrough** exists that a third party can replay step-by-step to the same end state."

Four separate defects in one contract:

1. **`command_log` is named as a determinism input and is not persisted.** `SYSTEMS.md:50` makes it a diagnostic-only artifact. So the stated reproducibility condition references an input that a normal save does not contain, and every saved world is in principle unreplayable.
2. **No test in the doc set exercises it.** `PROTOTYPE.md` §6.2 has seventeen headless test groups and none of them replays a command log. `PROTOTYPE.md:371`'s "replay step-by-step" is a human with a video, not a machine with a log. `C3` (`PROTOTYPE.md:266`) delivers "the same session delivered through the UI", which is a bus-vs-UI equivalence test, not a replay test.
3. **The system order has two incompatible sources of truth**: the numbered list in `SYSTEMS.md` §2 and "the composition root" in `ARCHITECTURE.md:134`. Nothing says which wins when they differ, and an AI session adding a system will append it to whichever it happens to be editing.
4. **The tie-break and the collection rule are under-specified in exactly the way that breaks across machines.** "ties broken by ascending instance ID" (`SYSTEMS.md:32`) does not say ordinal vs culture-sensitive comparison — in .NET the default `string.Compare`/`List<T>.Sort` on strings is culture-sensitive, so the same save can order differently under a different locale, and ULIDs are ASCII so ordinal is both correct and free. `SYSTEMS.md:37` requires systems not to read "iteration order of unordered collections" but never states a required collection policy; no document in the set contains the words `Ordinal`, `InvariantCulture`, or a numeric-type policy (`float` vs `double`) — grep-verified across all twelve documents. `PERSISTENCE.md:402` nevertheless asserts a **byte-identical** baseline across processes and 100 runs.

**Why it matters.** This is the same class of problem `ARCHITECTURE.md:53` solved for the seam by making it compile-time rather than conventional, left unsolved for determinism — which `D-05` calls "the single most dangerous constraint in the project" (`DECISIONS.md:130`). `PROTOTYPE.md:356` makes reproducibility part of Milestone 1's exit artifact ("a green test run"), and `RISK_REGISTER.md:354` makes it the first milestone deliverable; the mechanisms to verify it are (a) not persisted, (b) not tested, and (c) not scheduled.

**What I would do instead.** Make the command log a first-class P1 artifact: recorded for every save (it is a few KB), with a replay test that feeds it to a fresh world and asserts digest equality. State the full tuple once — seed, content hash, worldgen version, command log, system order, RNG model, float/double policy, comparison mode — pin ordinal comparison and `InvariantCulture` in `Directory.Build.props` and CI, and make the composition root the single normative order with `SYSTEMS.md` §2 as its documentation rather than a rival list.

---

### C-4 — A save never verifies that the baseline it is a delta of is the baseline that gets regenerated

**Evidence.**

> `PERSISTENCE.md:105` — `"worldgen_version": 3,        // generation-code version; frozen per content version`

> `PERSISTENCE.md:104` — `"content_hash": "sha256:9f3c…",// hash over the compiled content pack; cheap mismatch detector`

> `PERSISTENCE.md:402` — "| T-02 | Deterministic generation | Same `(seed, content_hash)` yields a byte-identical baseline across processes and 100 runs | **P1** |"

> `DATA_MODEL.md:597` — "**Baseline-locked surfaces** (generated cells, spawn placement, loot reproducibility) depend on `(seed, content_version)`. **Changing world-generation code or `placement` data requires a content-version bump**"

> `DECISIONS.md:130` — "**Hard requirement:** world generation from `(seed, content_version)` must be deterministic and version-stable."

The only thing standing between a player's save and silent corruption is a hand-edited integer. `content_hash` is computed from the content pack; `worldgen_version` is typed by a human. T-02 proves determinism *within a run and across runs of the same build*; it never persists a digest and nothing checks one at load. `PERSISTENCE.md:441` records the Windows commit as "untested on the target OS" and `RISK_REGISTER.md:56` specifies the CI digest test as RK-01's mitigation — a test that runs in a repository, not on a player's machine, and not on the load path.

Worse, the three documents disagree about what a generation change *is*. `PERSISTENCE.md:233` says worldgen "**must not change inside a content version**"; `DATA_MODEL.md:597` says changing generation code "requires a content-version bump" — i.e. is normal, sanctioned, and triggers migration. Pick one and the other becomes a bug.

The bound is also drawn in the wrong place: `ready_tick = last_harvest_tick + respawn_window(node_def) ± jitter(...)` (`PERSISTENCE.md:206`) makes a **content** value (`respawn_window`, `node_def`) part of the baseline-locked computation. A content bump therefore silently moves every stored respawn time, while `PERSISTENCE.md:210` claims "loading cannot change the answer".

**Why it matters.** Every failure mode in this finding is silent, and one of them — a generation tweak that does not bump an integer — corrupts every save simultaneously with all checksums verifying. `RK-01`'s rated impact is exactly this (`RISK_REGISTER.md:23`), so the finding is not the risk; it is that the mitigation is a convention in CI rather than a verification in the format.

**What I would do instead.** Content-address the baseline: store `worldgen_digest = H(generation assembly + placement data + runtime/FMA configuration)` in the manifest and verify it at load, before applying any delta, with refuse-or-migrate on mismatch. Then `worldgen_version` is a human label and not the authority. Reconcile `DATA_MODEL.md:597` against `PERSISTENCE.md:233`, and move `respawn_window` out of content or into the digest.

---

### C-5 — Tier transitions have no admission control, so the Tier A budget — on which `D-01`'s revisit condition and the whole frame budget rest — is not enforceable

**Evidence.**

> `WORLD_ARCHITECTURE.md:227` — "| Global live actor caps | A: 60, B: 300 | — | Enforced with spawn suppression, never with silent despawn of a diverged actor |"

> `WORLD_ARCHITECTURE.md:187` — "Budgets are enforced at two levels: **per-cell** … and **global live-actor caps** by tier (§6.2). If global caps are saturated, **spawn suppression** is applied by distance, then by `min` guarantee"

> `WORLD_ARCHITECTURE.md:288` — "| Hysteresis quota | At most N promotions and N demotions per tick (e.g. 4 each), priority-ordered by distance to the player and by `pinned` flag |"

> `WORLD_ARCHITECTURE.md:290` — "| Pinned exemptions | Actors in dialogue, in combat, quest-flagged, or within 25 m are **never** demoted by radius | Player-visible correctness beats budget |"

> `WORLD_ARCHITECTURE.md:262` — "Fall back to the anchor's authored stand point; if that fails, **refuse the promotion and keep the actor at Tier B**."

The only stated cap mechanism is *spawn suppression*, which is about creating new actors (`S-31`), not about promoting existing ones. Promotion is driven by player proximity, `pinned` reasons (dialogue, combat, quest flag), and a per-tick quota — none of which consult the Tier A cap. `§7.2` shows a promotion can already be **refused** for a legality reason, which proves the path exists, but `§7.4`'s hysteresis table and `§6.2`'s caps never connect. An actor can therefore be promoted into Tier A while the cap is full, and the cap is not the admission rule that the numbers in §12 depend on.

The exemption list makes saturation reachable in normal play, not just in stress tests: a settlement with 22 town NPCs (`VERTICAL_SLICE.md:118`, "4 km² with 22 town NPCs"), a fight, a quest NPC, and a 6-companion roster (`PROGRESSION.md:301`, "Max 6 active companions") can put a large number of `pinned` actors inside a 150 m radius simultaneously, and the doc's own answer is "reduce full-sim radius before reducing AI quality" (`WORLD_ARCHITECTURE.md:398`) — i.e. degrade the world to fit, which is the outcome the tier model exists to prevent.

**Why it matters.** Tier A cost is the load-bearing assumption under `D-01`'s only sanctioned engine-revisit trigger (`DECISIONS.md:46`, "Godot cannot sustain the required first-person frame budget … **after** the streaming and LOD work … is implemented — i.e. we have measured, not guessed"), under `WORLD_ARCHITECTURE.md:396` ("Main-thread world systems ≤ 4 ms") and under `RK-02`. A cap that the promotion algorithm does not consult cannot be measured meaningfully, because the measurement can be satisfied or violated by play pattern rather than by design. `WORLD_ARCHITECTURE.md:407` gives the response order for a missed budget, but the first lever in it — "reduce radii" — is the same lever that raises the number of promotion candidates per second.

The table the cap is supposed to be reconciled with is also internally inconsistent, which means the arbitration cannot be derived from it:

> `WORLD_ARCHITECTURE.md:222`–`225` — "| Visual streaming radius | **400 m** (9×9 cells) | … | Physics/collision radius | **120 m** | … | Full simulation radius (Tier A) | **150 m** | … | Regional simulation inner radius (Tier B) | **600 m** |"

Tier A is documented as running "Full AI, combat, physics, animation, pathfinding" (`WORLD_ARCHITECTURE.md:199`), yet the physics/collision radius (120 m) is *smaller* than the Tier A radius (150 m) — so actors between 120 m and 150 m are in full-simulation combat with no collision. Tier B (600 m) extends well beyond the visual streaming radius (400 m), so Tier B actors move across terrain that is not loaded. And tier is assigned by at least four parties with no stated precedence: `S-21` "owns … tier assignment per entity, streaming budget and hysteresis" (`SYSTEMS.md:228`), `S-31` "owns … spawn-tier assignment" (`SYSTEMS.md:318`), content declares `tier_hint: C  # preferred simulation tier when unobserved (D-06)` (`DATA_MODEL.md:218`), and `WORLD_ARCHITECTURE.md:293` makes cell tier "a separate assignment" from actor tier. Add the cap, the pinned exemption and the per-cell `budget.min` guarantee ("never suppress below `budget.min` in a Loaded cell", `WORLD_ARCHITECTURE.md:187`) and four rules claim priority over the same actor with no tie-break.

Finally, the hysteresis that is supposed to make all of this stable is history-dependent while tier explicitly is not persisted:

> `WORLD_ARCHITECTURE.md:287` — "| Minimum dwell time | 5 s in a tier before any further change |"; `:289` — "| Boundary cooldown | An actor that demoted within the last 10 s cannot be promoted by radius alone |"

> `SYSTEMS.md:230` — "**Persistent.** Nothing operationally (the tier of an entity is derived from player position at load)"

Three of the four hysteresis mechanisms (dwell time, last-demotion timestamp, quota consumption) are pure history, and tier is declared position-derived and unserialized. In the 150–188 m dead band of `§7.4`, a loaded actor's tier is therefore undefined at load and depends on whichever default the implementer chooses — a pop the register already concedes unit tests will not catch (`RISK_REGISTER.md:142`). The 10 s cooldown is also temporal rather than spatial, so a player who sprints out of a town and turns around has demoted every actor on the way and cannot re-promote any of them, producing a town that empties and refills — the exact symptom `§7.5` exists to prevent.

**What I would do instead.** Make the cap an explicit admission test on promotion with a stated rejection policy (queue, keep at B, or allow with a logged over-budget reason); declare one tier authority with stated precedence (pin > min-guarantee > cap > distance); assert the radius inequalities (`visual ≥ regional ≥ full-sim ≥ physics`) as validated invariants rather than prose; and either persist tier plus the two timestamps on the ULID-keyed record (a handful of bytes) or make the cooldown distance-based. Add `pinned` actors to the same test rather than exempting them from it. The debug overlay `WORLD_ARCHITECTURE.md:405` already requires should show the caps' headroom and the last transition reason.

---

### C-6 — The phase mapping contradicts itself across four documents, and the register's Phase-1 exit criterion declares the roadmap's Phase 1 a failure

**Evidence.**

| Document | Claim |
|---|---|
| `ROADMAP.md:392` | "`M1`–`M8` form the Phase-1 prototype; `M9` is the Phase-2 vertical slice" |
| `ROADMAP.md:332`–`333` | "| M7 | Building v1 | FEATURE | **1** |" and "| M8 | Dungeon, boss, multi-stage quest | FEATURE | **1** |" (the Phase column reads 1) |
| `PROTOTYPE.md:32` | "| Building / construction (any piece) | D-08 socket assembly is a *system*. It deserves its own proof, and it is not on the STEP 15 list. | Vertical slice |" |
| `PROTOTYPE.md:36` | "| Dungeons (instanced or interior) | The wolf den is an open cave mouth, not a dungeon. | Vertical slice |" |
| `PROTOTYPE.md:37` | "| Bosses, rare spawns, unique world encounters | … | Vertical slice |" |
| `PROTOTYPE.md:51` | "| NPC schedules, ecology, abstract regional simulation (D-06 Tier B/C) | … | Vertical slice |" |
| `PROTOTYPE.md:30` | "Anything not listed here is also out of scope by default." |
| `GAMEPLAY_LOOPS.md:281`, `:284` | "**BUILD** \| **Not in Phase 1.**" and "**RETURN** \| **Not in Phase 1.**" |
| `SYSTEMS.md:393`–`394` | Phase 1 includes "S-21 Streaming (single region)"; Phase 2 includes "S-04 Time, S-26 Relationships, S-27 Factions/Reputation, S-30 Exploration/Discovery, S-32 Buildings, S-34 Dungeons, S-35 Bosses…" |
| `RISK_REGISTER.md:198`, `:200` | "Phase 1 is **failed** if any system not on `PROTOTYPE.md`'s minimum list was built, regardless of how well it was built." … "the system may be excellent and Phase 1 still fails." |

Three independent documents (`PROTOTYPE.md`, `GAMEPLAY_LOOPS.md`, `SYSTEMS.md`) place building, dungeons, bosses, and Return in Phase 2 and declare Phase 1 closed to them. `ROADMAP.md` places M7 (Building v1) and M8 (Dungeon + boss) inside Phase 1 and on the critical path, and `ROADMAP.md:83` makes them prerequisites for the vertical slice ("`M7/M8 → M9`"). `RISK_REGISTER.md` then adopts `PROTOTYPE.md` as the authority and makes building-anything-else a Phase-1 failure.

`ROADMAP.md:362` gives a tie-break for a *different* conflict ("Where the two sequences differ, `ROADMAP.md` wins") — that clause is about task ordering in the register's own first-ten list, not about phase membership. Nothing anywhere resolves this one.

There is a smaller instance of the same class: `SYSTEMS.md:393` puts `S-21 Streaming` in Phase 1 while `PROTOTYPE.md:35` excludes streaming ("A second region, or world streaming … Streaming is a Phase 2 risk") and `ROADMAP.md:323` schedules `M3g Streaming cells + LOD + simulation tiers` as its own milestone.

**Why it matters.** `PHASE_0.md:9` makes the charter authoritative, and the reader is an implementation session. An AI session that starts from `ROADMAP.md` — the only document with ordered, citable milestones — will build Building and a Dungeon in Phase 1 and will produce a green M9, and by the register's own stated criterion every one of those systems is a Phase-1 failure. This is the scope failure `RK-10` predicts, produced by the documents rather than by ambition. It also has a direct cost: whatever is built twice, or built before its dependency, is the rework the roadmap exists to prevent.

**What I would do instead.** Declare one phase-membership authority and make the other documents cite it rather than restate it. Given `RISK_REGISTER.md:198` adopts `PROTOTYPE.md` and `GAMEPLAY_LOOPS.md` §15 agrees with it, the cheapest correct move is to re-cut the roadmap so M7/M8 are Phase 2 and Phase 1 ends at a milestone that matches `PROTOTYPE.md` §4 — or to amend `PROTOTYPE.md` and then re-issue the register's exit criterion, which currently says the opposite.

---

## MAJOR

### C-7 — Bulk demotion at hard load boundaries and interior entry violates the pinned-actor guarantee that companions depend on, and no transition rule exists across spaces

**Evidence.**

> `WORLD_ARCHITECTURE.md:130` — "Exceptions: on a hard load boundary (interior entry, fast travel, death respawn) the player's origin cell is force-promoted and the rest are **force-demoted** with the same reconciliation functions run in bulk"

> `WORLD_ARCHITECTURE.md:290` — "| Pinned exemptions | Actors in dialogue, in combat, quest-flagged, or within 25 m are **never** demoted by radius | Player-visible correctness beats budget |"

> `WORLD_ARCHITECTURE.md:274` — "Fine position → the nearest schedule anchor on the actor's route; the residual difference is discarded."

> `WORLD_ARCHITECTURE.md:309` — "An interior is its **own space** (`int.<def_id>`) with its own local cell grid"; `WORLD_ARCHITECTURE.md:315` — "Entering an interior is a **hard boundary**: the exterior is demoted in bulk (A→B or C)"

> `SYSTEMS.md:268` — S-25's only stated continuity commitment: "orders are validated against legality before acceptance, and an unexecutable order returns a reason rather than silently failing"

> `SYSTEMS.md:385` — "`UnlockNode(nodeId, method)`, `Travel(fromNodeId, toNodeId)`, `CreateAnchor`, `DestroyAnchor`"

Two guarantees conflict directly. `§7.4` says a pinned actor — including a companion in dialogue, in combat, or within 25 m — is never demoted, because "player-visible correctness beats budget". `§5.1` says a hard load boundary force-demotes everything not at the player's origin, "in bulk", and interiors are a *separate space* with no shared metre with the region. Nothing states whether the pinned exemption survives a bulk pass, and being a companion is not itself a pinned reason — the list is "dialogue, in combat, quest-flagged, or within 25 m".

So the state machine has two demotion paths and only one of them honours the guarantee. Consider the case the charter cares about most: a companion told to `hold position` (`PROJECT_CHARTER.md:602`) outside a dungeon, then the player enters the dungeon. On demotion its fine position becomes "the nearest schedule anchor on the actor's route" — but a companion following an order has no route and no schedule anchor. On exit, promotion places it at "a deterministic draw from `hash(ulid, game_tick, "promotion")`" inside an anchor region (`WORLD_ARCHITECTURE.md:262`). That is a teleport, which is `RK-07`'s defining symptom, arriving through the documented path rather than a bug. Neither `S-25`'s interface nor `S-38`'s `Travel(fromNodeId, toNodeId)` has any companion parameter, so nothing carries the companion across the boundary.

**Why it matters.** Companions are the pillar the charter substitutes for other players (`PROJECT_CHARTER.md:100`), and the acceptance bar is stated as reliability, not power — `ARCHITECTURE.md`-adjacent documents are unanimous that this is the load-bearing solo-first risk (`RISK_REGISTER.md:27`, `:116`). `RK-05`'s mitigation attacks power budget and abstract-tier inventory/combat (`RISK_REGISTER.md:122`), and `RK-07`'s mitigation attacks reconciliation of *position within a tier* (`RISK_REGISTER.md:154`). Neither addresses the companion's tier class or the cross-space transition, which is where a companion is most likely to be lost, and this is the failure a player will describe as "my companion teleported home" — a retention-level bug, in the one subsystem the register already rates Critical.

**What I would do instead.** Make companions a pinned tier class with an explicit rule ("never below the player's tier; fine transform persisted, never replaced by an anchor"), give `S-25` a recall/transition command and `S-38` a companion parameter, and state whether the bulk demotion pass obeys the pinned exemption before it is ever implemented.

---

### C-8 — Base defense and world events cannot legally occur off-screen, so two charter pillars have no tier-legal implementation

**Evidence.**

> `WORLD_ARCHITECTURE.md:244`–`245` — "| May **never** advance at Tier B/C | … Combat resolution of any kind | Damage, death, downing |"

> `WORLD_ARCHITECTURE.md:253` — "That second column is the whole safety argument: **abstract tiers never produce a state that Tier A cannot legally instantiate.**"

> `WORLD_ARCHITECTURE.md:343` — "| World events (caravans, incursions, migrations) | **Yes**, bounded | C | … events resolve abstractly and persist their outcome |"

> `WORLD_ARCHITECTURE.md:364` — "| Defense | Attack frequency, alert level and `disabled_attacks` persist. The charter requires players who dislike base defense to be able to greatly reduce or disable it … |"

> `SYSTEMS.md:328` — S-32 reads "S-12 (raid damage events)"; `SYSTEMS.md:331` — S-32 "emits `PiecePlaced`, `PieceRemoved`, `PieceDamaged`, `PieceDestroyed`, `BuildingCompleted`, `SettlementStateChanged`, `HomeAttacked`"

> `PROJECT_CHARTER.md:544` — "The player's property may occasionally face threats depending upon where it is built and what the player has done."

> `PROJECT_CHARTER.md:671` — "The world should occasionally create emergent situations: … merchant caravans attacked / settlements threatened …"

An off-screen raid or caravan ambush that resolves with no combat, no damage, and no death is not an event — it is a no-op. Piece damage is applied only through `S-12`, which `WORLD_ARCHITECTURE.md:250` forbids from running at Tier C/D; `§7.1`'s never-list forbids abstract death, damage, and faction-defining acts; and `SYSTEMS.md:331`'s `HomeAttacked` has no tier-legal producer. So either the home-defense and world-event layers are decorative (charter §14 and §18 unimplementable as written), or they violate the safety argument the document calls its whole justification for the tier model.

**Why it matters.** These are two of the ten charter features the world is supposed to be *about*, and they are the two whose entire premise is that they happen when the player is elsewhere. The document is honest about the general limitation — `WORLD_ARCHITECTURE.md:346` ("What does not continue is *action*") — but does not follow it through to the two systems that contradict it, and `RISK_REGISTER.md` has no entry for "a promised system is tier-illegal". The likely outcome is an implementer inventing an off-screen resolution rule, which is exactly the "abstract combat" the never-list exists to prevent.

**What I would do instead.** Define a closed set of abstract-resolution outcomes with an owner and their persistence (coarse attrition, a damaged flag, a repelled/overrun result), or state plainly that raids and ambushes require a Tier A cell and design the loop around player presence.

---

### M-1 — The `content_hash` fast path silently skips migration for content changes that move persisted derived values

**Evidence.**

> `PERSISTENCE.md:233` — "`content_version`/`content_hash` covers the pack: **equal hash means proceed with no migration**, and a differing hash runs the alias/tombstone pass (§6.3) plus the chain if `schema_version` also differs."

> `PERSISTENCE.md:97` — "`content_hash` is the fast path — on a match no migration is attempted at all"

> `PERSISTENCE.md:206` — "`ready_tick = last_harvest_tick + respawn_window(node_def) ± jitter(hash(node_key, harvest_seq, world_seed))`"

> `PERSISTENCE.md:210` — "Respawn is a pure function of persisted data plus world time, **so loading cannot change the answer**."

The fast path is sound only if no content edit can change the interpretation of persisted state. Several can, and none of them is a rename or a removal, so the alias/tombstone pass does not see them: tuning `respawn_window`, changing a `budget.min/max` band, changing an XP or price coefficient that a stored ledger was computed against, changing a `time_limit_min` that a quest deadline was derived from, or changing a difficulty constant behind a persisted counter. `PERSISTENCE.md:214` records the adjacent case as an explicitly unsolved open problem ("if respawn should later depend on *world* state … Any such dependency must be enumerated here before implementation"), but the *content-derived* version of the same dependency is not recorded anywhere, and it is already present in §5.5.

**Why it matters.** The failure is a silent reinterpretation of a player's existing save: the same bytes, a different `ready_tick`, no error, no quarantine, and no test. `DATA_MODEL.md:100`'s baseline test ("If a value is recomputable identically from `(seed, content_version)` with no player input, it does not belong in the save") is the rule that would prevent it; the mismatch is that the recipe for recomputation lives in content that `content_hash` treats as a mere mismatch *detector*.

**What I would do instead.** Split content into "shape" (definitions, renames) and "tuning" (coefficients that persisted values were derived from), and require a schema/migration step when tuning changes — or record a `derived_under_content_hash` per persisted derived value and recompute only when it matches.

---

### M-2 — A validated, documentation-only assumption is already false in the repository, and nothing in the doc set's own process caught it

**Evidence.**

> `SYSTEMS.md:416` — "**Superseded — all sibling Phase 0 documents now exist.** This assumption was written while the document set was still being produced in parallel and originally read "`PERSISTENCE.md`, `WORLD_ARCHITECTURE.md`, and `PROGRESSION.md` are not in the repository yet"."

> `DATA_MODEL.md:598` — "**Integrity:** the manifest carries a checksum over the state payload; writes are atomic (temp + rename) with at least one rolling backup; **a failed load never leaves a partially applied world**."

> `PERSISTENCE.md:120` — "**Note the deliberate omission.** The manifest carries **no checksum of itself**. The integrity root is `sections.sha256`, which covers every other file including `manifest.json`."

> `PERSISTENCE.md:335` — "| Quarantined section is `cells`/`entities`/`buildings` | Load **without** it … Partial recovery beats none |"

> `ARCHITECTURE.md:6` — "**Authority:** `PROJECT_CHARTER.md` governs creative intent. `DECISIONS.md` governs technical intent."

> `ROADMAP.md:95` — "**Entry:** Phase-0 documents exist and **are consistent with each other**."

`DATA_MODEL.md:598` asserts a manifest self-checksum that `PERSISTENCE.md:120` explicitly rejects as "a trap", and asserts "a failed load never leaves a partially applied world" against `PERSISTENCE.md:335`'s sanctioned partial load. Both are load/integrity contracts, in two documents each named normative for the implementer (`PERSISTENCE.md:5`, `DATA_MODEL.md:5`). Neither review pass that produced the present document set found them; `ROADMAP.md:95` makes consistency an *entry criterion* for M0, and `INDEX.md` records Phase 0 as complete.

**Why it matters.** The doc set's stated mitigation for documentation drift is that each document declares its authority and defers. That works when the conflict is visible. Here the conflict is in a one-line summary of a mechanism the other document discusses at length, in a document whose §1–§4 are exactly correct — so a reader trusts §6's summary and implements a checksum that must not exist. The process signal matters more than the instance: M0's entry criterion is not checkable as written, and `RISK_REGISTER.md:394`'s assumptions do not include "the Phase-0 set is internally consistent".

**What I would do instead.** Add a mechanical consistency gate for the doc set (cross-reference the version/integrity vocabulary — `schema_version` vs `save_version`, `content/aliases.yaml` vs `content/_aliases.yaml`, checksum location, partial-load policy) as an M0 exit artifact, and make each of these terms single-owner with the others citing it.

---

### M-3 — Two acceptance tests depend on time arithmetic that no document defines, and one of them depends on the very system its phase defers

**Evidence.**

> `PROTOTYPE.md:287` — "| C12 | gather both resource types, **watch a herb node refill after one in-game day**, and watch the iron seam *not* refill. | Either node behaves like the other. |"

> `PROTOTYPE.md:99` — "`node.herb.ashbloom` (respawns at +1 `world_time` day)"

> `PROTOTYPE.md:39` — "| Day/night cycle, weather, seasons | Time-of-day compiles down to "a number that advances"; **a static `world_time` counter is enough to test respawn.** | Vertical slice |"

> `PROTOTYPE.md:69` — "**Hard cap: 40 min** to see everything the prototype contains"

> `WORLD_ARCHITECTURE.md:173` — "`respawn_window_ticks: 72000        # ~1 game hour at 20 ticks/s`"

> `SYSTEMS.md:72` — "### S-04 Time & Calendar — **Phase 2**"

> `SYSTEMS.md:79` — "Despite the late phase, Phase 1 systems with durations hold plain counters and adopt the game clock when S-04 lands."

No document states how many ticks a game day contains, or what the in-game-to-real time ratio is. `WORLD_ARCHITECTURE.md:173`'s own arithmetic fixes 20 ticks per real second, so a "game day" of 72,000 ticks is one real hour — which contradicts the label "~1 game hour" and leaves the day length undefined at 1,728,000 ticks if a day is 24 real hours. Against `PROTOTYPE.md:69`'s 40-minute cap, C12's "watch a herb node refill after one in-game day" is either impossible (the day is longer than the session) or requires a time-skip that the document's static counter is not described as supporting. And the system that would own day length (`S-04`, "Game time (minutes since epoch), day/night phase, calendar date") is explicitly deferred to Phase 2 while the Phase-1 test depends on it.

**Why it matters.** This is the concrete form of the AI-session hazard the whole set is built to prevent: `PROTOTYPE.md:383` marks all numeric tuning values as "placeholders with a stated shape", but a *day length* is not tuning — it is the unit in which a passing test is expressed. An implementing session must invent it, and the invented value silently determines whether C12 passes, how long a player waits for a node, and whether `RK-12`'s "300 in-game days" catch-up test means 30 real days or 300. `GAMEPLAY_LOOPS.md:290` records the adjacent open item honestly ("(c) economy guard values … are tuning parameters … intentionally not numbered here"), but time itself is not listed as a parameter at all.

**What I would do instead.** Define ticks-per-day, the day/night phase function, and the real-time ratio as data constants in one place, independent of `S-04`, and state the prototype's day length explicitly so C12 is falsifiable.

---

### M-4 — The addressing formula the document calls "exact" returns out-of-range cell indices for negative coordinates

**Evidence.**

> `WORLD_ARCHITECTURE.md:59` — "### 3.3 Conversions (exact, no floating-point accumulation)"
> `WORLD_ARCHITECTURE.md:63` — "cellOf(world)     = ( floor(world.x / 100) mod 20, floor(world.z / 100) mod 20 )"
> `WORLD_ARCHITECTURE.md:67` — "`floor`, not truncation — truncation makes cells straddling the origin asymmetric, a classic off-by-one that produces a one-cell seam at world origin and nowhere else. **Every conversion must be tested at `x,z ∈ {-200.0, -100.0, -0.001, 0, 0.001, 100.0}`.**"

Evaluating the stated formula with C#'s remainder operator (which is what the implementer will write, and which is what `SYSTEMS.md:418` implies is implementation, not architecture):

| `x` | `floor(x/100)` | `% 20` in C# | required range (`WORLD_ARCHITECTURE.md:45`: `[0, 19]`) |
|---|---|---|---|
| `-200.0` | `-2` | **`-2`** | out of range |
| `-100.0` | `-1` | **`-1`** | out of range |
| `-0.001` | `-1` | **`-1`** | out of range |
| `0.0` / `0.001` / `100.0` | `0` / `0` / `1` | `0` / `0` / `1` | ok |

Three of the six mandated test values fail. The formula needs a floor-mod (Euclidean) rather than a truncated remainder: the correct values are 18, 19, 19. `WORLD_ARCHITECTURE.md:45` defines cell indices as "signed integers in `[0, 19]` **within** a region" and `WORLD_ARCHITECTURE.md:57` makes these keys into "filenames and CLI arguments in the persistence and tooling layers", so an out-of-range value is not a cosmetic error — it produces keys like `r_neg1_0:c_neg1_11` that no baseline generator, delta record, or navmesh bake will match.

**Why it matters.** It is a one-line bug with a save-format-shaped blast radius, in the function that every other subsystem's addressing, persistence sharding, and spawn budgeting depends on, and it is in a section labelled "exact". The document even anticipates the *floor-versus-truncation* trap and mandates the test values that catch it — while the remaining *modulo-sign* trap survives both the prose and the test list, because the test list does not state the expected outputs.

**What I would do instead.** State the expected output for each of the six values in the document, and specify floor-mod explicitly (`((i % n) + n) % n`). This is also the single cheapest thing to convert into the first unit test in the repository.

---

### M-5 — The reference implementation's single most-instructive file — the one that proves the YAML-in-one-file rule — exists only as prose

**Evidence.**

> `PROTOTYPE.md:126` — "| `item.tome.ember_primer` \| book \| **Read once → grants `spell.ember.bolt` + `spell.ward.oakskin`.** Proves content can grant capabilities. |"

> `DATA_MODEL.md:264` — "# also: channel: bool \| duration_min \| required_reagents?: [item.*] (consumed via S-14) \| persist_cooldown (default false)"

> `DATA_MODEL.md:389` — "**Closed reward kinds:** `xp`, `currency`, `item`, `spell`, `ability`, `recipe`, `reputation`, `relationship`, `title`, `access` (unlock location/service), `companion`, `property`, `world_flag`, `permanent_ability`, `transformation`."

> `DATA_MODEL.md:138` — "These examples are illustration, not content: when Phase 1 begins, each graduates into a real file under `content/` (S-18) and is deleted from this document, leaving the schemas behind."

The prototype's `item.tome.ember_primer` is the only content entry in the whole doc set whose behaviour is *read once and mutate character capability*, and the schema that would express it is never written down. `DATA_MODEL.md` §4.1's `ItemDefinition` example carries `note`, `durability_max?`, `requirements`, `affix_pool`, `enchant_sockets`, `set_ref` — no `on_read`/`grants`/`consumes`/`teaches`. The word "tome" appears in `DATA_MODEL.md` exactly once, in `PROGRESSION.md:199`'s prose about how magic mastery advances, not as a schema.

The same gap appears on the other side of the same file: `DATA_MODEL.md:608` says "**`custom_scripted` appears in §4.11 only to document its deliberate absence**", while `PROTOTYPE.md:126` requires a *content-driven state change to the character* that `§4.11` does not cover and no schema provides. `PROTOTYPE.md:328`'s anti-criterion is explicit that this must not become code: "an item, quest, or spell ID is hardcoded in C#; a new content *type* requires code (C7)".

**Why it matters.** `PROTOTYPE.md:275` (C7) is the criterion that proves the entire data-driven premise — "Adding one new item requires editing exactly one YAML file and **zero** C# files" — and the one item in the set that exercises the interesting half of it (`grants a capability`) has no schema. An AI session implementing `ember_primer` will either invent a field ad hoc (fragmenting the schema before it is frozen, with no validator coverage) or add a C# special case (failing the anti-criterion). Either way the reference content set stops being a proof.

**What I would do instead.** Add the "consumable that grants a capability" shape to `DATA_MODEL.md` §4.1 — most cheaply as one more entry in `§4.11`'s closed reward-kind list applied through an `on_use`/`grants` field — and make it the worked example, since it is the one the prototype depends on.

---

## MINOR

### m-1 — `WorldState` is named as the plausible god object, and the mitigation does not exist for the interface as specified

> `ARCHITECTURE.md:226` — "The thing most likely to become a god object by accident is **WorldState** — because it is the thing everyone can see. It is a *store*, not a *system*: it holds records and offers typed access, and it contains no decisions. Any `if` statement inside it is a bug."

> `ARCHITECTURE.md:220` — "Three **structural** defences:"

Defences 1 and 2 are structural (no `GameManager`; a registry whose narrowness is enforced by `PROTOTYPE.md:230`'s test). Defence 3 is the ownership convention attacked in C-1. The `WorldState` mitigation is "any `if` statement inside it is a bug" — a code-review rule with no check, in the document that argues at line 53 that conventions erode and compile errors do not. The named risk is correctly identified; the named mitigation is the one class of defence the document elsewhere says does not work.

**What I would do instead.** Either make the store a dumb typed dictionary with no query surface at all (so there is nothing to put an `if` in), or add the same CI check used for C4 to reject branching constructs in the store's source file.

---

### m-2 — Two documents publish different first-ten-task orders and different "who wins" rules; a third path is uncited

> `RISK_REGISTER.md:362` — "This list is the risk-focused view of the same early sequence and is **not** an alternative plan. … **Where the two sequences differ, `ROADMAP.md` wins**"

> `RISK_REGISTER.md:377` — "**Task 2 is first here, sixth there.**"

> `RISK_REGISTER.md:379` — "**`ROADMAP.md` §8 tasks 8 and 10 are not duplicated here.** … They are real tasks; they are simply owned by other documents."

The reconciliation is honest and explicit, which is why this is minor — but the register's task 2 (`RISK_REGISTER.md:365`, "Headless determinism probe") is the RK-01 validation that `ROADMAP.md` files as a risk spike inside M2, and the register's instruction "do not reorder them behind feature work" (`RISK_REGISTER.md:360`) is a constraint on the roadmap's order, not the other way round. Meanwhile the determinism probe depends on a generator (`ROADMAP.md:380`) that the register permits stubbing. Three documents, one sequence, one tie-break clause that does not cover the case it is most likely to be cited for.

**What I would do instead.** Merge to one numbered list with the risk view as an annotation column, as the two other parallel documents in this doc set already do for their own overlaps.

---

### m-3 — The tier model's own hazard list is not prefixed, so it collides with the register's numbering

> `WORLD_ARCHITECTURE.md:134` — "Bits are set by the mutation dispatcher, never by presentation (`D-11`). **`PERSISTENCE.md` §11 RK-02 records this as a live risk**"

> `RISK_REGISTER.md:11` — "All project-level risks are numbered `RK-01`..`RK-14` and this file is the **single risk authority**."

Project `RK-02` is the engine frame budget (`RISK_REGISTER.md:24`); the dirty-flag risk is `PERSISTENCE.md`'s local `RK-P02`, promoted to project `RK-11` (`RISK_REGISTER.md:220`). The unqualified `RK-02` in a world-architecture document reads as the engine risk, and the same document pairs `RK-A*` local ids with project ids elsewhere, so the prefixing convention is inconsistent exactly where it matters. In a doc set whose stated mechanism for AI-session safety is "cite decisions by ID rather than restating them" (`DECISIONS.md:10`), a mis-resolved ID silently points a reader at the wrong mitigation.

**What I would do instead.** Qualify every cross-document risk reference with its document (`PERSISTENCE.md RK-P02` / project `RK-11`), and treat an unqualified `RK-nn` as a lint error.

---

### m-4 — Tier B's own behaviour set is defined differently in two normative documents, and the register disagrees with `SYSTEMS.md` about when the transition risk is validated

> `SYSTEMS.md:417` — "Tier B = simplified regional (schedule + coarse position, no combat)"

> `WORLD_ARCHITECTURE.md:200` — "| **B** | Simulated regional | Coarse movement along authored/derived routes, schedule band evaluation, **simple threat/flee**, no combat resolution, no inventory, no dialogue |"

> `SYSTEMS.md:421` — "D-06's tier-transition risk ("NPC teleporting") is treated as a **Phase-2 validation gate**"

> `RISK_REGISTER.md:150` — "**Earliest inexpensive validation.** **Phase 1**: a scripted walk-past test."

`WORLD_ARCHITECTURE.md:200` gives Tier B simple threat/flee; `SYSTEMS.md:417` gives it none. Threat/flee at a tier that may not resolve combat is also the mechanism that would let an animal notice and flee the player at 300–600 m — a behaviour with no rule for what happens when the player gets close enough to see it. Separately, `SYSTEMS.md:421` defers tier-transition validation to Phase 2 while the register schedules it in Phase 1 and the roadmap files it as a Phase-1 spike inside M4 (`ROADMAP.md:329`, `:351`).

**What I would do instead.** State Tier B's behaviour set once, in the document that owns tiers, and have `SYSTEMS.md` cite it — and align the validation gate with the register, which the set elsewhere treats as the risk authority.

---

### m-5 — Interior entry is handled in the continuity rules but absent from the delta model it depends on

> `WORLD_ARCHITECTURE.md:315` — "Entering an interior is a **hard boundary**: the exterior is demoted in bulk (A→B or C) with the §7.3 checkpoints, the interior's entry cell is promoted to A, and pending dirty exterior state triggers a scoped save"

> `PERSISTENCE.md:154` — "cell: r_neg1_0:c_07_11        # global cell key (WORLD_ARCHITECTURE.md §3)"

> `PERSISTENCE.md:175` — "| `space` / `cell` | Where it lives (`D-05` sparse addressing) |"

`cells.msgpack`'s documented shape is keyed by a **region** cell (`r_neg1_0:c_07_11`), while interiors have their own key form (`int.<def_id>:c_02_03`, `WORLD_ARCHITECTURE.md:54`) and their own "delta record" (`:309`). `PERSISTENCE.md` never shows an interior cell in the delta, never says whether interiors share `cells.msgpack` or are sharded separately, and `PERSISTENCE.md:389`'s size budget ("`cells` … linear in cells the player *changed*") does not say whether interior cells count. Since interior entry also "triggers a scoped save" and is one of three hard load boundaries, this is on the hot path.

**What I would do instead.** Show one interior cell record in `PERSISTENCE.md` §5.2, and state whether interiors share the `cells` section and count against its budget.

---

### m-6 — Tier-transition side effects have no budget line and no owner, including the one rule that says a streamer may not discard dirty state

> `WORLD_ARCHITECTURE.md:141` — "| Cell demotes out of Loaded with dirty bits | The streamer may **not** discard. It requests a scoped save of that cell (or its shard), then discards. This is a correctness rule, not an optimization |"

> `WORLD_ARCHITECTURE.md:397` — "| Streaming work per frame | ≤ 3 ms, spread over ≥ 4 frames per cell |"

> `PERSISTENCE.md:371` — "**(3) Cadence cap.** At most one autosave per 60 s and never two in one frame"

> `RISK_REGISTER.md:33` — "`RK-11` … the flags are set in one place by design, but "one place" is a convention"

A player who fights through a cell and then sprints crosses several demotion rings, each of which forces a scoped save, while the autosave cadence cap says at most one per 60 s and the streaming budget has no line for save work. Two declared constraints with no arbitration: the correctness rule is unconditional ("may **not** discard") and the budget is a performance target with no owner for the conflict. This is distinct from `RK-11` (missing flags) and `RK-02` (general frame budget) — it is a mandatory unbudgeted side effect of a routine player action.

**What I would do instead.** Give the scoped save its own budget line and a stated policy for the collision with the cadence cap (defer the *streamer*, not the save — and say so), and note that a streamer which can block on I/O is a `RK-02`-class frame-time risk.

---

## The three things I would change before writing any code

**1. Resolve the load/integrity contract into one numbered, normative sequence — and fix the boot order.**
`ARCHITECTURE.md` §8, `SYSTEMS.md` §2 (`S-33`) and `PERSISTENCE.md` §7 currently give three different answers about when baselines are regenerated, whether a partial load is legal, and whether `manifest.json` checksums itself, and the one order written as "follow exactly" covers writes only. Nothing else in the set can be implemented unambiguously until this is one document with one sequence: `S-03`, `S-20`, `S-33`, `D-05` and every persistence test in `PERSISTENCE.md` §10 all execute it. It is also the cheapest fix here, because it is prose, not code.

**2. Make ownership and determinism enforcements rather than declarations, and schedule both tests before the code they constrain.**
C-1 and C-3 are the two places where the architecture's own central argument — conventions erode, compile errors and tests do not — is not applied to itself. Narrow `IWorldState` per system (or assert an owner on write), pin ordinal comparison / culture / numeric type in a `Directory.Build.props` and CI, persist a command log with a replay test, and put the ownership test next to `RISK_REGISTER.md:366`'s architecture test in M1/M2. Doing this before Phase 1 feature work is nearly free; doing it after Phase 1 is the rewrite `D-02` was chosen to avoid.

**3. Cut Phase 1 to something one or two people can finish, and make one document the phase authority.**
`ROADMAP.md`'s M1–M8 (building, dungeon, boss, factions, streaming, 22-NPC settlement) and `PROTOTYPE.md`'s minimum (a 200 m square, 39 definitions, one quest, one companion) are different projects, and `RISK_REGISTER.md:200` says the roadmap's version fails. `PROTOTYPE.md:70`'s 10-day figure excludes the majority of its own critical path, `SYSTEMS.md:393` fields 27 systems in Phase 1, and no milestone in M0–M16 carries a duration at all. Before code: pick the phase authority, re-cut the roadmap to match it, and give every milestone a size. This is the one decision that cannot be recovered cheaply later, because Phase-1 code is production code by `ROADMAP.md:393` ("The "prototype" label refers to content scope, never to architecture quality").

---

## Risks I believe are MISSING from `RISK_REGISTER.md`

Distinct from `RK-01`..`RK-14` and from the missing-risk lists in the two sibling reviews.

1. **Slice-ownership erosion inside the domain (C-1).** `RK-09` covers presentation→domain and player-global assumptions, both structurally caught. Nothing covers domain→domain cross-slice mutation, which the seam tests cannot see and which `ARCHITECTURE.md:135` calls the most common way authority boundaries rot. *Highest-value addition.*

2. **Load-path correctness as a distinct risk from save-format correctness (C-2, C-4).** Every persistence risk in the register is about what is written (`RK-01`, `RK-11`, `RK-13`, `RK-06`) or about time passing (`RK-12`). None covers *ordering and verification on the read path*: baseline regeneration vs delta application, derived-cache recomputation vs migration, alias resolution vs cross-reference validation, partial-load reference integrity. `PERSISTENCE.md` §7.1 shows the team understands that write order is a correctness property; the read path has no equivalent.

3. **Determinism is not yet a mechanism (C-3).** `RK-01` covers determinism failing; nothing covers determinism being *unverifiable in production* — no persisted baseline digest, no persisted command log, no replay test, no pinned comparison/culture/numeric policy, and no scheduled owner for any of them. The CI digest test in `RISK_REGISTER.md:56` detects a change in a repository, not a mismatch on a player's machine.

4. **Tier-A admission control (C-5).** `RK-02` is the frame budget and `RK-07` is transition discontinuity; neither covers the cap being unenforceable because the promotion algorithm does not consult it, which is what makes an `RK-02` measurement non-reproducible across play patterns.

5. **Documentation phase-membership conflict as a scope vector (C-6).** `RK-10` rates scope as the default failure but attributes it to ambition and prescribes discipline. The register's own exit criterion makes the roadmap's Phase 1 a failure, so the conflict is a *cause* of scope failure and is not merely discipline-shaped — it needs a document fix, not a review checklist.

6. **Game-time units undefined (M-3).** No document fixes ticks-per-day or the real-time ratio, yet `RK-12`'s validation, `AG-2`'s "per real day", `PERSISTENCE.md:206`'s respawn window, quest deadlines (`SYSTEMS.md:302`, "deadlines as absolute game time") and `PROTOTYPE.md` C12 are all expressed in that unit. Every one of them is untestable until it exists.

7. **Silent reinterpretation of persisted derived values by content tuning (M-1).** `content_hash`'s fast path (`PERSISTENCE.md:97`) assumes no content edit can change the meaning of saved bytes. No register entry covers the tuning-versus-shape distinction, and `PERSISTENCE.md:214` records only the world-state version of the same dependency.

8. **No defined protocol for merging or retiring a progression axis.** `DECISIONS.md:191` and `PROGRESSION.md:49` both name the trigger ("if two axes always move together … they are one axis and must be merged"), and `ROADMAP.md:364` prices the consequence ("Merging axes late means re-balancing every character and save") — but no document defines how per-axis state is migrated out of live saves when it happens. It is a save-schema change with no migration story, discovered at G-2.

9. **Demotion-triggered scoped saves as a mandatory, unbudgeted side effect (m-6).** `WORLD_ARCHITECTURE.md:141` makes the scoped save unconditional while `§12`'s budget table has no line for it and `PERSISTENCE.md:371` caps autosave cadence. A correctness rule and a performance budget collide with no arbiter — not `RK-11`, not `RK-02`.

10. **Interior tiering relative to the player (C-7).** `WORLD_ARCHITECTURE.md:315` promotes only the interior's entry cell to Tier A and bulk-demotes the rest; no interior radii, quotas, or promotion rules are specified. In a 1 km² interior — which `§2` explicitly permits — a boss or quest NPC is at Tier B/C while the player is inside the same space, where `§7.1` forbids it resolving combat and `§7.2` allows its promotion to be refused outright. Distinct from `RK-14` (seams) and `RK-07` (transition discontinuity within a space).

11. **Player buildings invalidating authored NPC schedule anchors.** Placement legality validates "terrain slope, socket graph connectivity, no blocking of authored story geometry, no overlap with a pinned spawn" (`WORLD_ARCHITECTURE.md:360`) but not schedule anchors, while `S-24` persists "anchor reassignment after building changes" (`SYSTEMS.md:257`) with no command, event, or consumer. A player who builds over `anchor.*` leaves an NPC whose promotion target is inside geometry — which `§7.2` step 2 explicitly rules out — with no defined outcome. Distinct from `RK-06` (building persistence/navigability) and `RK-14` (cell seams).

12. **Promotion-quota starvation.** `WORLD_ARCHITECTURE.md:288` caps promotions at ~4 per tick, priority-ordered by distance to the player, with no aging or dwell-based escalation. A fast-moving player can demote an actor and then never re-promote it, because every candidate nearer the player wins the quota first. `RK-07` covers the visible symptom class, not this mechanism, and no test in the set targets a moving player across a boundary.

13. **Offline production has no tier owner.** `WORLD_ARCHITECTURE.md:367` ("a forge with fuel and an assigned smith produces at Tier C"), `GAMEPLAY_LOOPS.md:237` ("production is not advanced abstractly without staffing and inputs") and `SYSTEMS.md:329` (S-32 persists "farm plot crops and growth deadlines", which advance on a clock regardless of staffing) are three different answers to what advances while the player is away. Guard `E-6` is the economy's defence against offline minting, and no field or system decides which Tier C production is actually advanced.
