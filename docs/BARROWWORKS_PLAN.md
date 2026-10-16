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
| 2026-10-16 | `5f591c8` | P1b: a site's cell follows the space it stands in + 3 tests | 860 passing, 0 failed |
| 2026-10-16 | `1cfbf48` | P1c: portals and the return anchor + 9 tests | **869 passing, 0 failed** |
| 2026-10-16 | `1b39e1b` | P1d: the space system, wired into the simulation + 9 tests | **878 passing, 0 failed** |
| 2026-10-16 | `0cc2c89` | P1e: persistence deferred, with a guard test; space stays runtime-only | **879 passing, 0 failed** |
| 2026-10-16 | `8524f64` | P1f: the position-derived cell sweep, 4 sites + 3 tests | **882 passing, 0 failed** |
| 2026-10-16 | `888d67d` | P1g: the gate, `--visual barrowworks=1` passed down as a GameOptions flag | **884 passing, 0 failed** |
| 2026-10-16 | `28af843` | P1h: `RegionLayout.SpaceId`, and site cells follow the layout's space | **886 passing, 0 failed** |
| 2026-10-16 | `c4b9dbe` | P2a: `space:` on the region schema, and the 13-room blockout | **890 passing, 0 failed** |
| 2026-10-16 | `8750d66` | P2b: the two gated entrances, through real content | **896 passing, 0 failed** |
| 2026-10-16 | `_P2c_` | P2c: the thirteen rooms as named places, and the survey's stations | **897 passing, 0 failed** |

### P2a — `space:` on the region schema, and thirteen rooms that connect

**An interior is just another region.** That is the whole design: `content/regions/barrowworks.yaml` is a
region content file like any other, declared with `space: space.barrowworks` and cells written
space-qualified. No new content kind, no new loader path, and the existing `structures` / `doors` /
`bounds_m` / `terrain` schema carries the blockout unchanged.

**What the schema gained:** an optional `space:` key, read into `RegionLayout.SpaceId`, plus two checks
that use it — every listed cell must be in the declared space, and the bounds-coverage walk now resolves
points with `CellKey.OfInterior` rather than `CellKey.OfWorld`. Absent, it is the exterior, so every region
that existed before is byte-identical. That closes the last two sites P1f deliberately left.

**The blockout is generated, not typed.** `tools/gen_barrowworks_blockout.py` holds the room table and edge
list from this plan's sections 1 and 1.1 and derives the geometry: four walls per room with a 3 m opening
centred on each, and a 3 m corridor per edge. 13 rooms, 14 edges, 120 structures, 196 lines of YAML.
Hand-typing a hundred-odd boxes would have made the connectivity graph unreviewable, which is the one thing
a blockout exists to make checkable.

**Two real errors the checks caught, both worth recording:**

1. **The content validator refused the file:** `WLD007: the spawn point must be inside the bounds and clear
   of every structure`. The corridors ran centre to centre, so a corridor box passed straight through R01's
   middle and contained the spawn. Fixed by clipping each corridor leg to the *gap* between the two rooms'
   outer rectangles. A validator catching a geometry mistake before a screenshot was taken is exactly what
   it is for.
2. **The rooms needed shifting.** R04's west wall sits at x = -47 m in the design's own metres, and cells
   index from 0, so the whole interior is offset by (+50, +10). The design's coordinates are preserved in
   the generator's table; only the blockout's are shifted.

**Proven by walking, not by looking.** `BarrowworksBlockoutTests` flood-fills the walk space from the
spawn at 1 m steps, using `Blocker.Separation` with the body's own radius — the same test movement uses, so
a gap a body cannot fit through is not counted as a route. **All thirteen rooms are reachable.** That is the
one failure a screenshot cannot show.

**This is a GREYBOX and is not finished art.** Openings are on all four sides of every room on purpose —
a blockout exists to make circulation testable before the real doorways are placed. G1, G2 and G3 arrive
with the content port (P4). No lighting pass, no materials, no dressing. Visual acceptance for P2 is
**BLOCKED** pending the owner's review, and will stay so until P3's art kit replaces this.

**Superseded by P2b:** the entrances now exist, so a player can reach the interior. See below.

### P2e - the screenshots were never about the interior: the worktree had no imported assets

**The owner's testimony was right and my measurement was wrong.** They watched every Godot launch and said
there was *never* a world - not dark, absent - and that it had never changed. I had measured a 976 KB frame
falling to 52 KB and read that as "my light made it worse". It was noise in a frame that was already empty.

**The decisive experiment I should have run first:** render the **exterior**, ordinary play, no Barrowworks
involved at all. It came back **mean 3.09**, with 23,019 pixels above 32 in a 2,016,000-pixel frame. The
exterior of the game's own starting region was as black as the interior. **The Barrowworks was never the
subject.**

**Root cause.** `src/Presentation/.godot/` is gitignored, so a fresh worktree has **no import cache at all**:

| | `.godot/imported` |
|---|---|
| shared checkout | **2,271 files** |
| this worktree | **missing** |

Godot does not render unimported assets. Every frame I took - exterior and interior, gated and ungated - was
of a project with nothing to draw. `src/Presentation/addons/` is gitignored too and was also incomplete (3
addons against 23); copying those changed nothing on its own, so the addons were a red herring and the
import cache was the whole of it.

**The fix, and the evidence it is the fix:** run one import pass in the worktree
(`godot --headless --path src/Presentation --import`, 2,269 files), then the same exterior shot again:

| | mean luminance | pixels above 32 |
|---|---|---|
| exterior, no import cache | **3.09** | 23,019 |
| exterior, imported | **100.86** | 1,938,945 |

**Grass, sky, the character, a shadow, an ash hound and a rock formation** - the game, rendering, in this
worktree for the first time.

**What this means for the phases already done.** Nothing in P0-P2's *code* was wrong: the blockout builds,
its walls and doorway are where the generator put them, collision works, the space identity holds, the gate
is enforced, and all 897 tests pass. But **every visual claim I made before this is void** - in particular
P2d's "the blockout renders correctly, it is merely unlit", which was an over-read of an amplified empty
frame. The honest position is that until this round **no render had been seen**, and P2's screenshots must be
re-taken and re-judged from zero.

**A setup step the brief did not mention and no document recorded.** A fresh worktree of this project needs
two gitignored directories populated before it can render: `.godot/` (imported assets, via an import pass)
and `addons/`. Anyone building a second worktree will hit this. It is recorded here so they do not.

**The interior is genuinely dark** - now that there is a world to compare it to, the room frames show walls,
floor and character lit faintly by a blue sky ambient that does not reach an enclosed space. That finding
survived the fix; the lighting work it implies is real, and was reverted this round because it was measured
against a frame that could not show its effect.

### P2d - the screenshot pass, and what it found

**The survey command, for reuse** (GPU lock held for the duration, released after):

```
powershell -ExecutionPolicy Bypass -File C:\Otherreach_Recovery\gpu_lock.ps1 acquire "deepseek <job>"
C:\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe ^
  --path src/Presentation -- --region region.barrowworks --content-root content ^
  --visual barrowworks=1 --shot "50,10,0" --shot-out C:\Otherreach_Build\barrowworks_shots\trial
powershell -ExecutionPolicy Bypass -File C:\Otherreach_Recovery\gpu_lock.ps1 release "deepseek <job>"
```

`--shot` takes `x,z,yaw_deg[,pitch][,seconds]` and stands the character on that spot; `--survey <dir>` does
every location the region authored, which is now all thirteen rooms.

**What the first frame showed, and how it was read.** The run reported the character standing at (50, 10) in
R01 at Y = 0.00, then reaching (50, 2.5), and failing to reach (50, 0.4) - so collision and the walk space
are real. The frame itself came back **black below the HUD**, which the owner saw live.

Black is two very different findings, so the frame was measured rather than guessed at: **1,629,817 of
2,016,000 pixels (81 %) are non-zero**, and lifting the levels by 24x and auto-contrasting resolved the
picture. It shows **the blockout drawn correctly** - two wall forms with the centred 3 m doorway between
them, the character standing in the room, the floor under them. The geometry, the openings and the
character placement are all right.

**So the blockout is built and renders; it is simply unlit.** The survey deliberately uses the game's own
sun, sky and camera rather than the audit's built `Environment` ("the pictures worth looking at are
gameplay pictures"), and a windowless interior gets no sun. Nothing is broken - there is no interior light,
because until now there was no interior.

**P2's screenshots are therefore BLOCKED on interior lighting, and this is not finished.** A black frame is
not evidence a reviewer can accept, and calling the blockout photographed would be false. Interior lighting
is a real feature (a light the interior declares, or an ambient the space applies) and is the next piece of
work, not a tweak.

**Not attempted:** the lighting itself. Started and stopped here deliberately rather than begun at the end
of a long session - it changes how the game draws a space, which is exactly the kind of change that deserves
its own pass with its own measurement.

### P2c — the thirteen rooms as named places, and the survey's stations

**The blockout had no places, and `SurveyShots` builds one camera station per location a region authored.**
So the per-room screenshots the phase asks for did not merely need a camera — they needed the rooms to be
**discoverable places**, which is content P4 needs anyway: a room the player cannot discover is not a room.

**Landed:** thirteen `location` content files under `content/locations/barrowworks/`, one per room, each with
its display name, its centre in the blockout's metres, a 6 m discovery radius and the room's own purpose in
its notes — all from the plan's section 1, which comes from the frozen level design. Nothing invented.

**The checker is the point.** `EveryRoom_IsANamedPlace_SoTheSurveyHasThirteenStations` asserts the region
builds exactly thirteen places and that each sits at its room's centre. That is what makes the screenshots
possible, and it is the same assertion P4 will lean on.

**A stored-but-unused field removed.** `LocationSite.Space`, added in P1b, was never read: the layout's own
`SpaceId` drives `CellOfSite`, so a per-place space was a second source of truth that no rule consulted.
Removed rather than left. `DoorSite.Space`, `SwitchSite.Space` and `BarrierSite.Space` stay — `Simulation.CellOf`
reads those.

**One redundancy recorded rather than fixed:** a door, switch or barrier now also sits in a region whose
`SpaceId` is known, so their own `Space` is a second source of truth too. It is read, so it is not dead; but
the two can disagree, and `TryBuildLayout` does not yet reject that. Left for P4, where the doors G1–G3 are
authored and the disagreement would first be possible.

### P2b — the two gated entrances, and a player who can walk in

**Content now carries the entrances.** `content/regions/starting_frontier.yaml` gained a `portals:` section
and the region schema gained the reader for it — a key, the space it leads to, an exterior anchor and an
interior anchor. Three checks: the key is namespaced, the space is not the exterior, and the exterior anchor
is walkable ground in the region that declares it.

**The gate is now exercised for real.** P1g's filter was vacuous while no content declared a portal. It is
not any more: with the Barrowworks off, both authored entrances are dropped and the interior is unreachable;
with it on, both are registered and both work. `WithTheGateOff_EnteringIsImpossible` walks to A's anchor and
is refused.

**Six tests, and they are the phase's acceptance:** entering by the threshold arrives at R01's centre in the
blockout; each entrance leaves to *its own* anchor through real content rather than a fixture; the two are
not at the same place; and entering is impossible with the gate off. `EachEntrance_LeavesToItsOwnAnchor` is
the design's named failure tested against content rather than against a hand-built fixture, which is what
makes it worth having twice.

**PROVISIONAL PLACEMENT — the owner should move these (OQ-11).** The anchors are the canon features the
brief names — Blackvein's spoil heaps, and The Sinking Moss — and those two places are **610 m apart** while
the interior is 137 m from R01 to R04. At that distance the interior is a shortcut between two distant
places rather than two ways into one dungeon. Placed as instructed and recorded rather than silently
"fixed"; the smallest change is one pair of numbers.

### P1h — a layout is one space, so its sites follow it

**Landed:** `RegionLayout.SpaceId` (exterior by default) and `SystemContext.CellOfSite`, which resolves an
authored site's coordinates in the layout's own space. Six sites converted: the fixed-node cell in
`Crafting`, the spawn-site cell and the creature record's host cell in `Creatures`, a location's cell in
`Quests`, a place's flag cell in `Social`, and a place's tier cell in `Discovery`. `Items` was converted
too, and its private `CellOf` helper — which served both sites and a body — is gone, split into
`CellOfSite` for the two container callers and `CellOf(body)` for the ground drop.

**Why on the layout and not on six site records.** A layout *is* one space: its nodes, spawns, containers,
stations, NPCs and named places are authored together in one coordinate frame. Putting a space on each site
record would let them disagree with each other and with the layout holding them — a state no rule covers.

**Deliberately not converted:** `WorldContent.cs`, which *builds* the layout and so has no layout to read a
space from. That one is P2's, when an interior layout exists to build.

**A near-miss worth recording.** Converting `Items.cs` truncated it to zero bytes. The Python opened the
path with `io.open(path, 'w')` — which truncates on open — and then failed on an illegal `newline` argument,
so the file was emptied and never written. `git checkout --` restored all 777 lines from HEAD, verified
byte-identical, and the conversion was redone with the edit tool.

The lesson is specific and cheap: **never open a source file for write in a script.** Write to a temporary
and move it, or use the edit tool. `git checkout` was the only reason this cost minutes rather than the
file. Every earlier script in this session that used the same pattern was one bad argument away from the
same outcome.

**Tested:** a layout is in the exterior unless it says otherwise; an interior layout's site coordinates
resolve to a space-qualified cell and not to the exterior ground those metres would otherwise name.

### P1g — the gate, and why it is a flag rather than an option read

**The brief names the spelling:** `--visual barrowworks=1`. **D-11 forbids the obvious implementation:**
`VisualOptions` is Presentation, and the projects reference **Presentation -> Application**, so nothing in
World or Application can read it. The two constraints have one synthesis, and it is also the layering's
correct shape:

- `VisualOptions` gains `barrowworks` (`0`/`1`, default `0`) — the switch, spelled as the brief names it.
  `Defaults` is what `--visual phase_a` copies, so a Phase-A run excludes it too.
- `GameOptions` gains `Barrowworks` — the gate, on the record Application already owns.
- `Main.cs` reads the switch once at boot and passes it down.

Presentation parses the command line; Application owns what content the simulation holds. Neither layer
learns the other's concepts, which is what D-11 is actually protecting.

**It is enforced, not merely read.** `GameSession` clears `RegionLayout.Portals` unless the gate is set, so
a portal declared in content is inert without `--visual barrowworks=1`. A forgotten flag cannot leak the
interior into a normal launch, and the guarantee does not rest on content discipline.

**Honest limitation:** with no content declaring entrances yet, the filter is currently vacuous — it is
exercised for real when P2 authors the two entrances. What is *not* vacuous today is the switch, the flag,
the pass-down and the default-off assertion, all of which are tested.

**Tested:** `GameOptions` defaults the gate off; a session booted without it carries the flag false and
declares no portal. `VisualOptions` itself is untested here because its `Parse` calls `GD.Print` and needs
the Godot runtime; the launch in P2 is what exercises the parse.

### P1f — the position-derived cell sweep

**What the sweep found.** `RngChannel.Open` seeds from `CanonicalHasher` over the cell's canonical string,
and that string carries the space. So the random stream was **already** space-separated by P1a, with nothing
added — a channel is keyed by the cell, and the cell knows its space. The design's requirement holds
structurally rather than by a rule anyone has to remember.

**So the sweep reduced to one question: is the cell passed in space-qualified?** Eleven `RngChannel.Open`
sites and every flag and delta lookup were checked. Most already come from a space-carrying source —
worldgen, a node, a spawn site, a container site. **Four derived a cell from a bare `Body`:**

| Site | Was | Now |
|---|---|---|
| `Combat.cs:851` | `OfWorld(at.XMm/1000, at.ZMm/1000)` | `_context.CellOf(at)` |
| `Companions.cs:622` | same | `_context.CellOf(at)` |
| `Crafting.cs:192` | same | `_context.CellOf(body)` |
| `Creatures.cs:480` | same | `OfInterior(State.Space, …)` |

Plus the two tier lookups (`Companions.cs:627`, `Creatures.cs:981`), which are worse than a wrong roll: a
body in the wrong cell asks for the wrong tier, so the wrong cells get simulated.

**One helper, not five edits.** `SystemContext.CellOf(Body)` resolves a body's cell in the ambient space,
which is the player's. That is correct while one space holds every actor — the Barrowworks is a single shared
instance, its rooms are always reached through the player, and the design freezes an absent space rather than
simulating it. A space recorded per creature is what a second concurrently simulated space would need, and
the remark says so.

**Three tests make separation a property rather than a hope:** the same local coordinates in two spaces draw
different values; a channel is still stable across opens in one space, so determinism survived; and two
interior spaces do not share a stream.

**Left alone deliberately.** The remaining `CellKey.OfWorld` calls that take a *site* (`Crafting.cs:72`,
`Creatures.cs:179`, `Items.cs:679`, `Quests.cs:262`, `Social.cs:410`, `Systems.cs:471`) are correct for a
region that is one space, and become wrong only once a region's layout itself belongs to an interior. The fix
there is a space on `RegionLayout` rather than on each site type — P2 work, once the rooms are authored. The
presentation-only calls (`Main.cs`, `GroundField.cs`, `HollowView.cs`, `SoundEvents.cs`) read the exterior
world and need nothing.

### P1e — persistence deferred, deliberately

**Attempted and reverted:** the space wired all the way through persistence — `PlayerRecord` fields, the
player DTO, a schema 14 → 15 bump and a `SchemaV14ToV15` step.

**Why it was reverted.** `Fixtures/README.md` requires a schema bump to land as one complete unit: a new
`v15/` fixture written by the new writer through `M2.Probe`, the previous current section shapes frozen as
`Sections/V14` with the step producing 14 repointed at them, and every `expected.json` whose state gained a
field updated — and it warns never to change one to make a failing migration pass. Reverting is what took
Persistence.Tests from 11 failures back to 173/173.

**Why deferring is safe.** The state is unreachable: no region declares a portal, so `EnterSpaceCommand` can
never succeed in a shipped run. `TheSpace_IsNotPersistedYet_SoItMustStayUnreachable` asserts that, so the
deferral cannot rot silently, and it fails the moment a portal is declared without the bump beside it.

**The hazard, stated rather than left implicit:** a save made inside would load with the body at
interior-local coordinates while the simulation believes it is in the exterior — the collision the space
identity exists to prevent, in reverse. OQ-10 carries this and the smallest choice that avoids it.

### P1d — the space system, wired into the running simulation

**Landed:** `StateSlice.PlayerSpace`; `RuntimeState.Space` and `.Traversal`; `EnterSpaceCommand` and
`LeaveSpaceCommand`; a `SpaceSystem` owning the slice; and `RegionLayout.Portals`. The transition now runs
through a real simulation: entry places the body at the portal's interior anchor, and leaving returns it to
the exterior anchor of the portal that was used.

**Verified end to end, nine tests against a running arena:** both entrances lead to the same space; each
round-trips to its own anchor; the two exits are asserted to differ (the design's named failure); being too
far is refused and the character does not move; an unknown portal, a second entry and a bare leave are all
refused; and a region with no portals declared changes nothing.

**A refusal leaves the character where they were.** The system returns a reason rather than throwing —
the recoverable coherent snapshot the design asks for when a transition cannot complete — so a refused
entry cannot half-move a body or strand a traversal.

**`SetSpace` enforces the pairing:** space and traversal are written in one call and must agree. Going to
the exterior while still holding a traversal, or entering without one, throws. A body in a space it did not
arrive in is a state no rule covers, so it is unrepresentable.

**Default-off intact and asserted:** every region declares no portals and the player starts in the exterior.

**Harness note for the next person:** these tests run against a bare `Arena`, not a `GameSession`, because
the space system is simulation-side. Arena metres are the hollow's, so region millimetres convert through
`Harness.HollowOf`. The first attempt drove `session.Submit` and failed with *"there is no running world"* —
an arena builds its own simulation and does not attach it to the session.

### P1c — portals, and the return anchor the design warns about

**Landed:** `SpacePortal`, `SpaceTraversal` and `SpacePortalSet` in `src/Domain/Spatial/SpacePortals.cs`.
A portal is an exterior anchor and the interior anchor it arrives at; entering stores **which portal** was
used, and leaving returns to *that* portal's exterior anchor.

**Why it is one field and not a coordinate.** The design names this failure explicitly: *"A player
returning via B must not be snapped to the saved entry A because a single global return vector was
reused."* So the traversal records the **portal key**, never a position. Storing the coordinate as well
would let the two disagree, and the design is equally clear that the stored entry is not a reason to force
a particular exit. `Leave` throws rather than falling back to another anchor — a refused exit is better
than an invented one, and a test asserts exactly that.

**The Barrowworks' own two entrances are the test fixture:** the hill above Blackvein and the Sinking Moss
drain mouth, at the coordinates the design gives. Round-tripping through either ends where it started, and
the two exits are asserted to differ.

**Still not wired to anything.** No transition command, no runtime state, no save. This is the rule written
down and proved; connecting it to the simulation is the next piece.

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

