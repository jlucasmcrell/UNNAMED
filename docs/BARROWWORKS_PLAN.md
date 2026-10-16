# Barrowworks plan (P0)

**Branch:** `feature/barrowworks`, worktree `C:\Otherreach_Build\barrowworks_wt`
**Base:** `restore/beast-oct02-snapshot` @ `e805ff3`
**Baseline verified green before any change:** build 0 errors; `dotnet test src/UNNAMED.sln` = **847 passed, 0 failed** across 8 suites.
**Author:** DeepSeek, per `DEEPSEEK_PROJECT_BARROWWORKS.md`.

The dungeon is "The Unfinished Measure": a service ring of thirteen rooms under the Ashen Hollow hill that
was built to gauge something and never finished the job.

**Nothing here is visible in a normal launch.** Every entry point is gated behind `--visual barrowworks=1`.
With the option off the playtest must behave exactly as it does today.

---

## 1. The thirteen rooms

Stable IDs R01–R13 and centres come from `03_RECOMMENDED_LEVEL_DESIGN.md`; connectivity from
`04_LEVEL_MAPS.md`. All coordinates are **interior-local X/Z metres, real Y up**. Interior-local, not
world — that distinction is the whole of P1.

| ID | Name | Centre | Clear | Purpose and player beat | Content |
|---|---|---|---|---|---|
| R01 | Threshold | (0, 0) | 10×10 | Entrance A. Split stone lintel, worn delivery groove. A safe look into the hall, not at an enemy | Exterior portal A; orientation plaque |
| R02 | Intake Hall | (0, 18) | 14×12 | First choice: open west service mouth vs visibly latched east grille; the scale arm is visible through the grille, previewing the shortcut | **K1** withdrawal notice; route fork; **G1** (closed) |
| R03 | Wash Passage | (-22, 28) | 12×10 | First threat. Shallow wet floor against a dry safe edge; the spider is visible before its detection boundary | **E1** one spider; bypass edge ≥3 m; alternate entry |
| R04 | Drain Mouth | (-42, 28) | 10×10 | Genuine alternate entrance B. Narrow exterior approach opens inside; sightline to R03 stops before R05 | Portal B, no quest gate, same instance/delta as A |
| R05 | Sorting Floor | (-22, 50) | 18×14 | Escalation: two work tracks divide around a low non-blocking tray. Observe, separate, or fight | **E2** two spiders, separated perception, no forced simultaneous pull |
| R06 | Still Bay | (0, 50) | 14×12 | Quiet beat and midpoint orientation: silent bench, repair fragments, forward warning | No enemies or hazards |
| R07 | Gauge Archive | (22, 50) | 14×12 | Discovery: changed graduation marks, fitting diagram, visible shelf gap. The craft is learned here | **K2** fitting note; **P** diagram rack; demonstrator wedge; reveals **G2** |
| R08 | Return Gallery | (22, 28) | 10×14 | Earned shortcut: operate a reachable mechanical latch; daylight/dust cue leads back to R02 | **G1** opens from this side; no enemy spawns |
| R09 | Blind Store | (42, 50) | 10×10 | Secret reward: inspect the shelf gap, push the cover, find a discarded earlier repair | **K3** optional; modest fixed supplies; **G2** discoverable panel |
| R10 | Service Vestibule | (0, 74) | 14×12 | Pre-boss. A solid dogleg removes all firing lines into the arena; audible slow strikes precede danger | **E3** one Husk on the inbound connector, not on the recovery marker; safe reset marker |
| R11 | Measuring Court | (0, 96) | 28×24 | Boss and mastery. Wide plain floor, readable ring markings, broad turning room | **B1** Ring Custodian; **G3** rear service gate unlocks on defeat |
| R12 | Headworks | (0, 121) | 16×10 | Aftermath and choice. Damaged head, two explicit handles, information before confirmation | Choice consumes the crafted wedge; outcome cache; no new enemy |
| R13 | Inspection Bay | (22, 74) | 12×10 | Optional combat/lookout loop; a screened view hints at arena proportions without allowing shots | **E4** two spiders; ordinary fixed supply |

**Arena sub-layout** (from `04`): bounds X ±14, Z ±12; entry at (0,-12); rear G3 at (0,+12); boss home
(0,3); initial encounter boundary (0,-8); perimeter lanes X ±10 (not immune zones); two shallow
recesses at (-13,0) and (13,0) that interrupt a line strike but stay reachable without a choke.

### 1.1 Connectivity

```
                          R12 HEADWORKS [choice/cache]
                                |
                          G3 boss defeat
                                |
                          R11 COURT [B1]
                                |  always retreatable; solid dogleg
                          R10 VESTIBULE -------- R13 INSPECTION [E4]
                                |                        |
R05 SORTING [E2] -------- R06 STILL ---------- R07 ARCHIVE ----G2---- R09 SECRET
      |                                            |
R04 DRAIN B --- R03 WASH [E1]                     R08 RETURN
                   |                               |
                 R02 INTAKE ----------G1----------+
                   |
                 R01 THRESHOLD A
```

| Edge | Type | Length est. |
|---|---|---|
| A–R01, B–R04 | Portal, load boundary | distinct exterior anchors, shared interior |
| R01–R02 | Critical | 18 m |
| R02–R03 | Critical | 25 m |
| R03–R04 | Alternate entrance | 20 m |
| R03–R05 | Critical | 22 m |
| R05–R06 | Critical | 22 m |
| R06–R07 | Investigation | 22 m |
| R07–R08 | Return discovery | 22 m |
| R08–R02 | **G1** earned shortcut | 25 m |
| R07–R09 | **G2** secret | 20 m |
| R06–R10 | Critical | 24 m |
| R07–R13 | Optional | 24 m |
| R13–R10 | Optional loop | 22 m |
| R10–R11 | Critical / retreat | 28 m routed |
| R11–R12 | **G3** aftermath | 25 m |

Loops: **L1** R02–R03–R05–R06–R07–R08–R02 after G1. **L2** R06–R07–R13–R10–R06.
All three gates open permanently. R11–R10 is never sealed, including during combat.

### 1.2 Routes and pacing

Usual first pass: A → R01 → R02 → R03 → R05 → R06 → R07 → R08, open G1, then back through R07/R06 to R10.
Boss may be fought before or after retrieving the pattern. R09 and R13 are optional and never hold a
required consumable. Both entrances work in both directions from first use.

Targets to measure: first entrance → first choice 30–90 s · quiet intervals 20–45 s · internal retry A
to R10 via G1 **35–55 s** · completion with optional route 70–110 min.

---

## 2. System pieces to build

Classified against `12_SYSTEM_DEPENDENCIES.md`. D01–D09 already exist at main; the new work is D10–D15.

### P1 — the interior space system (D10, the load-bearing piece)

This is the part everything later depends on, and the Undermeasure and fold passages will reuse it.

**The core change: coordinates become space-qualified.** `12` §"Interior integration details" is explicit
that this is not a portal-scene concern. `CellOfLocation`, gathering-site cell derivation and crafting
RNG keys currently derive from **exterior** world coordinates. Two equal local coordinates in different
spaces must never interact or share a random stream.

Every position- or cell-bearing runtime path must be reviewed, not just the portal:

- combat target membership
- projectiles
- audio and perception
- inventory reach
- stations and nodes
- discoveries and flags
- companion orders
- baseline identity
- serialization

**What an interior is:** its own space with portal anchors and a return anchor stored in the player's
save. Shared single instance across A and B. `persistent:false` — frozen while absent, so absence runs no
combat. No reset, no timer repopulation, no offline catch-up.

**Enter/exit contract** (World §8): entering checkpoints dirty exterior state and promotes interior
cells; exiting checkpoints interior deltas and returns to the **selected** exterior anchor. Exiting B
after entering A arrives at B's exterior anchor — a single global return vector must not snap the player
back to A. A failed destination load must leave a recoverable coherent source snapshot and must never
half-transfer inventory or companion.

**Multi-entrance topology is one interior identity.** Migration and alias support must handle renamed
interior, portal and content IDs with no manual save editing.

**Reuse, don't parallelise:** the existing region/cell loading and `SwitchRegion` if present. A second
loader is the failure mode here.

Tests go in `tests/` beside the existing suites, and must cover: two spaces with equal local coordinates
not interacting; RNG streams not being shared across spaces; return-to-B from-A; save and load inside;
frozen-while-absent; and a failed destination load leaving the source coherent.

### P4–P5 — the smaller extensions

| ID | Piece | Note |
|---|---|---|
| D11 | Boss encounter/phase/attack lifecycle | Data-driven phases, tick tells. No behaviour-tree framework, no adds |
| D12 | Inspection facts and `produced_item` / named-defeat quest predicates | K1/K2/P/A effects, reads ProductionFirsts, queries D11. Not a script engine |
| D13 | Guarded choice, item consumption, common reward/cache/act commit | Atomic cross-owner operation, single outcome, exactly-once grants |
| D14 | Space transfer and active-attempt recovery anchors | Validated R10 marker, portal-specific exterior destinations, attempt-reset restaging preserving Wait/downed |
| D15 | Space-aware journal/map prompts and explicit boss-reset message | Minimal presentation binding; existing UI surfaces may host it |

**State ownership** (`12` §"State ownership boundaries"): S-34 owns portals, G1/G2, shared interior
progress, the no-reset policy and derived G3. S-35 owns active attempt, permanent B, and defeat identity.
S-29 reads facts for the journal and owns no combat or inventory. Progression owns recipe/XP. Inventory
owns haft/wedge/cache transfers. Presentation owns prompts and evidence capture only.

**Boss load exception** (from `08`, normative): an active-attempt load moves the player to R10, clears
attempt-only effects and hazards, restores the boss baseline, and retains saved player resources. A
completed defeat stays completed. Death keeps the existing Ashen Waystone behaviour.

---

## 3. Asset list with sources and licences

From `11_ASSET_REQUIREMENTS.md`. **A file on disk is a candidate, not an accepted asset.**

### 3.1 Blocker to resolve first: the depot drive letter moved

`11` gives every depot path under `F:\Otherreach_External_Assets`. **`F:` does not exist on this
machine.** The depot is now `C:\Otherreach_External_Assets`. Paths in the source document are therefore
stale and must be resolved by filename against the current root, not by drive letter. Recorded in
`OPEN_QUESTIONS.md` — it is a documentation defect, not a design one.

### 3.2 Depot candidates — CC0, verified licence text must be re-read at import

| ID | Asset | Source path (drive-corrected) | Licence |
|---|---|---|---|
| AR02 | Rough rock faces | `PolyHaven\models\rock_face_01` | CC0 |
| AR03 | Dressed ruins/columns | OpenGameArt RonKaPaun Fantasy Ruins Pack | CC0 — **reject gothic religious motifs** |
| AR04 | Work pipes/clamps | `PolyHaven\models\modular_industrial_pipes_01` | CC0 — sparse fittings, not a spaceship corridor |
| AR05 | Storage crate | `PolyHaven\models\wooden_crate_01` | CC0 |
| AR06 | Wall/base material | `ambientCG\...\Concrete034` | CC0 — not bare modern concrete everywhere |
| AR07 | Worn metal | `ambientCG\...\MetalPlates006` | CC0 |
| AR14 | Boss motion reference | `Quaternius\Universal Animation Library[Standard].zip` | CC0 — **already on disk and already verified in this session** |

Additional CC0 caves, rock wall scan and minerals named by the brief live in
`C:\Otherreach_External_Assets\Sketchfab_20261016\` (Kalamos Cave, Rock Wall Scan, calcite, limonite
dripstone, salt stalagmite, malachite, industrial pipe packs) — **these are new since the design was
written and are not in `11`.** They are the better fit for the deep rooms and should be considered first.
`Stylized_Borderline_20261016\SHORTLIST.md` carries ratings for the Rotopoly sewer kit, the Nodex3D
medieval dungeon kit and Kyle's Rock Pack.

### 3.3 To author (greybox first, all NEW PRODUCTION)

| ID | Item | Count |
|---|---|---|
| AR01 | Room shell — **6–8 reusable structural modules, not 13 unique room meshes** | 6–8 |
| AR09 | Latch, shelf cover, headworks — 3 simple rigid assemblies, no physics | 3 |
| AR10 | K1/K2 record boards, P diagram surface, optional note — text stays live UI, not baked image | 3–4 |
| AR11 | Fitting wedge — simple authored wood tool mesh and icon | 1 |
| AR13 | Ring Custodian body — rigid segmented construct, 2.6 m working height, greybox acceptable for proof | 1 |
| AR15 | Three readable telegraph shapes + impact dust | 3 |

### 3.4 Reuse as-is

AR08 existing work props via `art_bindings.json` (respect the withheld list — bad bellows and wall
swatches stay withheld) · AR12 `content/creatures/beast/cave_hunting_spider.yaml` and
`content/creatures/undead/bone_walker_husk.yaml` · AR16 player/Tavar untouched · AR18 existing journal
and combat UI · AR20 existing inventory and iron-ingot/ash-haft definitions.

### 3.5 Licence rules I will hold to

Only **CC0, CC-BY (recording the credit), MIT or Fab Standard**. GPL, NC, share-alike and "no AI" never
ship. **Fab, Megascans, Cosmos, MetaHuman and WildMesh content must never enter a generative model** —
no img2img, no Trellis, no texture generation, no vision-model enhancement. Placing them in the engine is
fine. ComfyUI only on our own or CC0 inputs. Re-texturing uses the box/triplanar method already proven in
`Stylized_Borderline_20261016\_renders\retex_test.py` with PolyHaven and ambientCG PBR.

Anything Fab-sourced in the dungeon gets a Megascans/Old Mine or Derelict Corridor provenance note, and
is excluded from any generative step.

### 3.6 Budget

6–8 structural modules; 6 reused prop families; 3 interactive assemblies; 4 readable surfaces; 1 tool; 1
boss body; ~8 boss motion states; 3 telegraph shapes; 3 unique tell sounds. Few material families: stone,
worn metal, wood, damp ground. These are authoring counts, not draw-call guarantees.

**Frame time target:** hold the current High-tier frame time inside the dungeon, and measure it.

---

## 4. Content port (P4)

Old content: `implemented/content/quests/barrowworks/unfinished_measure.yaml` — **`schema: 1`, which is
the same schema the current quests use**, so this is a port rather than a rewrite. Port it; do not
redesign it.

**14 objectives** in the existing graph: `o_offer` → `o_read_notice` / `o_reach` → `o_read_fitting` /
`o_read_diagram` → `o_custodian` → `o_haft` → `o_wedge` → `o_open` → `o_measure` → `o_preserve` /
`o_dismantle` → `o_done` → `o_report`.

**5 locations to define** (`content/locations/`, schema confirmed against `blackvein_cut.yaml`):
`location.barrowworks.intake_hall`, `.gauge_archive`, `.service_vestibule`, `.headworks`, `.blind_store`.
These are **interior** locations, so their anchors must be space-qualified — directly dependent on P1.

**9 flags** (`content/world_flags/`, schema confirmed against `cw_house_01_door_open.yaml`):
`withdrawal_notice_read`, `fitting_note_read`, `diagram_rack_read`, `gate_g3_open`, `headworks_preserved`,
`headworks_dismantled`, `blind_store_read`, plus `fact:defeat.creature.boss.ring_custodian`.

**Reader order is free by design:** a reading done before the quest is offered counts, and a wedge made
ahead of being asked counts. K1 and K2 are required evidence; K3 is optional and no objective asks for
it. The choice is the design's two endings and **either completes the quest — neither is a failure**.
Dismantle gives 2 ingots and pleases Kera; preserve pleases Sel and sets the moving-gauge flag.

The Codex fragment (archive) and the Lantern's Foot (Blind Store) are placed as **items with world flags
only**. The Lantern questline is not mine.

**`lint` and `xref` must report 0 errors.** Rewards stay marked PROVISIONAL / NOT BALANCE-APPROVED.

---

## 5. The Ring Custodian (P5)

Level 5, 150 HP, three telegraphed attacks, a rear weak point. Built on the existing Animated Armour
creature base as a **behaviour reference only** — the Phase-B checkpoint says its geometry is NOT FIXED.
Data-driven phases, tick-owned tells, no adds, no general behaviour-tree framework. Uses real recorded
sounds from the existing audio library. No environmental interactable is needed to make it vulnerable.

---

## 6. What I will skip, and why

**Safe cuts, per `12`:** K3 prose richness · R13 supply dressing · optional Tavar barks · deeper vista ·
ornate pipe set · non-essential diagram art. R09 stays a secret area even if its reward is small.

**Out of scope by dependency:** D17 character creation and UI settings · D21 rest/respawn and player
Downed · D22 utility power and offline simulation · D23 the full Lantern epic and Warden aura · D24
vertical nav, swim, climb, physics collapse, procedural dungeon grammar.

**Explicitly not touched:** travel (map, Waystones, binding) · MetaHuman, garments, characters · existing
NPC dialogue except one new Kera branch gated on my quest flag · anything under `content/regions/` beyond
the two gated entrance placements · TTS and voice nodes · `src/Presentation/Audio/` and the audio
manifests. **Dungeon audio is requested as a list of sound ids in the handoff report, not implemented.**

**Also not doing:** no new lore. Open design points go to `OPEN_QUESTIONS.md` with the smallest unblocking
choice. New lore proposals, if any, go to `canon_readonly/proposals/new/`, never into `canon/`.

**Flatness is deliberate scope control:** no stairs to walkable upper decks, ladders, jump gates, moving
platforms or physics puzzles. Boss floor stays convex and unobstructed. No freestanding central pillars —
creatures are unpathed and cannot negotiate them.

---

## 7. Risks and how I will handle them

| Risk | Response |
|---|---|
| **P1 is the critical path and the largest unknown** — six days estimated | The interior system is the one thing that must be right; it is not cuttable. If it slips, stop after P1 and P2 rather than starting a phase I cannot finish green |
| **Space-qualifying coordinates touches many call sites** | Do it as one coherent change with tests, not incrementally, so there is never a half-qualified build. `tests/Architecture.Tests` will catch boundary violations |
| **`F:` depot paths are stale** | Resolve by filename against `C:\Otherreach_External_Assets`; do not trust the drive letter |
| **`assets/` is DeepSeek-owned but AGENTS.md forbids committing it from a gameplay branch** | Keep P3 art in the depot and `content/` bindings; do not stage `assets/`. Flagged in `OPEN_QUESTIONS.md` |
| **GPU is shared and the driver has crashed five times** | Every render under `gpu_lock.ps1`, released in `finally`, ≤10 min batches while the queue is non-empty |
| **Disk** | Keep C: above 60 GB. Currently 98.3 GB free; the 21 GB low-poly library could be moved to the new drive when it appears |
| **Boss visual acceptance cannot be claimed** | Greybox proof only. If production review is pending, mark visual acceptance BLOCKED rather than calling it finished |

---

## 8. Phase order

Each phase must leave the branch building and green. Stop cleanly at the end of any phase.

| Phase | Deliverable | Gate |
|---|---|---|
| **P0** | This document | Written |
| **P1** | Interior space system (D10) + tests | 847 baseline tests still pass, new tests pass |
| **P2** | 13-room blockout, both entrances, G1, Blind Store, collision, lighting | Screenshot every room through `gpu_lock` |
| **P3** | Art kit | Frame time held; licences recorded |
| **P4** | Content port | `lint` and `xref` both 0 errors |
| **P5** | Ring Custodian | Three telegraphed attacks readable, rear weak point |
| **P6** | Handoff report | At `C:\Otherreach_External_Assets\REPORT_FOR_CLAUDE_BARROWWORKS.md` |

**Done means:** with `--visual barrowworks=1` a player can enter from either entrance, clear 13 rooms,
beat the Custodian and make the choice, find the Blind Store, save and load inside, and leave to arrive
back where they entered. With the option off, nothing in the playtest changes and every suite passes.

**Not merging anything.** Claude reviews and merges.

---

## 9. Immediate next actions

1. ~~Write `OPEN_QUESTIONS.md`~~ — done, commit `eb5ff85`.
2. Read `05`/`06` (quest and state graph), `08` (boss), `09` (encounters), `13` (slices) and `14`
   (acceptance) before P1 lands, so the system is shaped by what it must carry.
3. **Resolved while implementing P1a, and it changed the plan:** `VisualOptions.cs` states in its own
   header that *"every option chooses how the game is drawn, never what the simulation holds (D-11)"*.
   Gating the **entrances** is a simulation change, so putting `barrowworks` in `VisualOptions` would
   violate that contract even though the brief offers `--visual barrowworks=1` as the example. The gate
   belongs on `GameOptions` (Application owns content booting), set from a launch flag in `Main.cs`,
   mirroring `VisualOptions`' *style* — declared allowed values, explicit default, refuse an unknown key
   by name. Recorded as OQ-09. Trivial to move under `--visual` if Claude prefers the literal spelling.
4. **No `SwitchRegion` exists.** There is no region-switching path to reuse, so P1's enter/exit
   transition is new code rather than an extension of one. `RegionLayout` and the cell/baseline path in
   `src/World` are what the system has to work with.

---

## 10. Progress log

| Date | Commit | What landed | Suite |
|---|---|---|---|
| 2026-10-16 | `eb5ff85` | P0: this plan and `OPEN_QUESTIONS.md` | 847 passing |
| 2026-10-16 | `b9234da` | P1a: cell identity is space-qualified + 8 tests | 855 passing, 0 failed |
| 2026-10-16 | _(this commit)_ | P1b: a site's cell follows the space it stands in + 3 tests | **860 passing, 0 failed** |

### P1b — sites carry their space, and `SpaceId` moved to Domain

**Landed:** `DoorSite`, `SwitchSite`, `BarrierSite` and `LocationSite` each carry a `Space`, defaulting to
the exterior. `Simulation.CellOf` resolves a site's flag cell in that space. A door, a switch and a
barrier are exactly where a `world.*` flag lives, and a flag cell is where the flag, its delta and its
baseline hash live — so before this, an interior grille at local metres would have opened the exterior
gate sharing its coordinates.

**`SpaceId` had to move to `src/Domain/Spatial/`.** It was written beside `CellKey` in `World`, and the
build refused it: `RegionLayout` is Domain, Domain cannot reference World, and the four site records
need the type. Domain is the innermost layer that World and Content can both see, so a space identity
belongs there. This is `tests/Architecture.Tests` doing its job — the layer boundary is enforced, and it
caught a placement mistake rather than a boundary violation.

**Why the sites and not `Body`:** `Body` is referenced at roughly 250 sites as a bare position. Putting a
space on it would have been a sprawling change across combat, companion, creature, quest and presentation
code. A site is authored content that genuinely knows where it stands, which is the narrowest place to
put the fact, and it is what `CellOf` actually reads.

**Not done, still the bulk of P1:** the player's own space as runtime state; the saved return anchor; the
enter and exit transitions; the frozen-while-absent lifecycle; save and load while inside; and the sweep
of the position-derived cells that do *not* come from a site — `Crafting`, `Creatures`, `Combat`, `Social`
and `Items` each derive a cell or an RNG channel straight from a body's coordinates and will need the
space of whoever is standing there.

### P1a — what landed, and what did not

**Landed:** the identity change. `CellKey` now carries a `SpaceId`. The exterior stays the default and its
canonical key spelling is unchanged, so every existing cell key, delta record, baseline hash and
historical save fixture is untouched — the whole suite, fixture tests included, is green with **no
migration**. `OfInterior` names a space; `OfWorld` keeps its exterior-only meaning, so no existing call
site changed behaviour.

**Verified by test, not by inspection:** the same local coordinate in two spaces is a different cell and
hashes differently; two interiors do not collide; interior keys round-trip; exterior keys are
byte-identical; a space name containing `/`, `:` or whitespace is rejected rather than written.

**A real bug caught by the suite:** a struct's `default` runs no constructor, so `default(SpaceId).Name`
is null and `IsExterior` dereferenced it — `NullReferenceException` on the first parse, because
`TryParse` starts from `default`. Fixed by treating null as the empty exterior name. This is exactly the
class of failure the tests exist to catch, and it would have been invisible in review.

**Not done, and it is the bulk of P1:** the portal anchors and the saved return anchor; the enter and
exit transitions with dirty-exterior / promote-interior checkpointing; failure handling that leaves a
recoverable source snapshot; the frozen-while-absent lifecycle; save and load while inside; and the
space-qualification of every cell-bearing runtime path named in `12_SYSTEM_DEPENDENCIES.md` — combat
membership, projectiles, perception, inventory reach, stations, discoveries, companion orders.

**The next step is not more code in `Coordinates.cs`.** It is to sweep the ~30 `CellKey.OfWorld` call
sites and decide, per site, whether it is exterior-only or must become space-aware. The answer differs
per site — `SoundEvents` and `GroundField` are presentation-side and may not need it at all, while
`Crafting`, `Creatures`, `Combat` and `Social` derive RNG channels and almost certainly do — so it is a
review, not a mechanical edit.

