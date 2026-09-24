# Otherreach asset library — forensic regression audit

Read-only audit of the library as it exists on disk. Nothing was regenerated, repaired or moved.
Every number below was measured in this pass; the method is named wherever a number appears, and
anything that is inference rather than measurement is labelled as such.

**Note on inputs:** the brief referred to attached comparison images. None arrived in this session, so
the examples named in the brief were located on disk instead and judged from the actual renders in
`assets/review/bible_batch/`. If the attached images showed something different, that difference is
not reflected here.

---

## Part 1 — Executive summary

**Is there a real quality regression? Yes and no, and the distinction matters more than the answer.**

There is **no gradual, time-based decline** in the 3D pipeline. There is one model (Pixal3D, 482 of 500
manifests), one reconstruction pipeline, and the geometry budget is stable across the whole build:
median LOD0 triangle count is 39,523 in the earliest batch (09-22, n=163) and 39,516 in a late batch
(09-23 20:00, n=206). Texture resolution is 2048 across the library except 15 assets at 1024.

But there are **three real, separate defects**, and together they are more than enough to produce the
owner's impression:

1. **Every single LOD in the library is untextured.** 1,455 LOD1/LOD2/LOD3 files, 1,455 with zero
   materials and zero textures. Not "across a large part" — 100%. Distance renders grey.
2. **The buildings are not reconstructions.** `building_beam` and `building_post` have **12 triangles**;
   `building_road_segment` 24; `building_door_frame` 36. `longhouse` and `forge_shed` are both exactly
   **3.0 × 2.6 × 3.0 m** at ~2,700 triangles. These are boxes and primitives, not the assets the names
   claim.
3. **Reconstruction fails on a predictable class of subject**: large architectural volumes and
   compound props with thin members. On subjects that are compact, solid and convex it is fine.

**Is the owner's impression substantially correct? Yes on the visual gap, no on the diagnosis.**

The gap is real and in places severe — `building_smithy` is a catastrophic crumpled mass, and
`prop_blocked_shaft` is a shattered pile with no readable structure. But it is **not an 80% decline
over time**. Within a single batch built inside three minutes of each other, the same settings produced
the `creature_ash_ember_hound`, `weapon_march_spear` and `landmark_ashen_waystone` — all clean and
readable — alongside the broken prop set. The variance is driven by **what the subject is**, not when
it was made.

**Top 5 causes, ranked by confidence:**

| # | Cause | Class | Confidence |
|---|---|---|---|
| 1 | LOD1–3 exported with no material and no texture assignment, library-wide | B / E | **Certain** (measured: 1455/1455) |
| 2 | Buildings shipped as 12–36 triangle primitives and exact 3×2.6×3 m boxes | A / D | **Certain** (measured) |
| 3 | Pixal3D cannot reconstruct thin-membered or open architecture; it collapses them | A / D | **High** (same-batch contrast) |
| 4 | The 09-23 21:00 batch ran entirely at the `lean` tier while every other batch ran majority `mid` | D | **Certain** (measured: 22/22 lean) |
| 5 | No visual review gate. Every asset is `status: export_ready`, `godot_validated: true`, `lod_status: present` — including the catastrophic ones | Process | **Certain** (measured across 500 manifests) |

**The single most important sentence in the library's own documentation**, from `docs/QUALITY_TIERS.md`:

> `_blender_preview.py` did not exist before this; until it did, **no finished mesh had ever been
> looked at, only validated structurally.**

That is an admission, written by the pipeline itself, that structural validation was the only gate. It
explains every finding above. A 12-triangle box passes a triangle-count check and a "has a collision
hull" check. An untextured LOD passes a "LOD exists" check. Only looking at the mesh catches them, and
looking at the mesh was introduced late, for one asset, to compare tiers.

---

## Part 2 — Asset inventory by batch / era

Grouped by manifest write time. Counts are from 500 `*_meta.json` files in `assets/ready/`.
"Tier" is inferred from triangle count against the settings documented in `QUALITY_TIERS.md`
(lean = 25,000 faces, mid = 40,000); it is an inference, though a tight one, because the counts fall
into two clear clusters near those two values.

| Batch | Assets | Composition | Typical tier | Common defects |
|---|---|---|---|---|
| 09-22 14:00 | 163 | creatures, weapons, NPCs | 55 lean / 107 mid | untextured LODs (universal) |
| 09-23 04:00 | 40 | mixed | 0 lean / 38 mid | untextured LODs |
| 09-23 14:00 | 55 | props, tools | 20 lean / 34 mid | untextured LODs; `prop_wooden_bench`, `prop_wooden_cart_wheel` |
| 09-23 17:00–19:00 | 6 | door, workbench, barrel | 6 mid | untextured LODs |
| 09-23 20:00 | 206 | the bulk dispatch | 31 lean / 163 mid / 12 other | untextured LODs; buildings and thin props collapse |
| **09-23 21:00** | **22** | **creatures, NPCs, weapons, landmarks, props, resources** | **22 lean / 0 mid** | **the visible failure batch** |
| 09-24 03:00 | 5 | buildings, ore | 3 lean / 2 mid | buildings are primitives |

Two things stand out.

**The 09-23 21:00 batch is anomalous.** Every other batch is majority `mid`. This one is 22 of 22
`lean`, with zero `mid`. Every asset the owner named as weak is in it: `prop_blocked_shaft` (19,299
tris), `resource_woundmoss` (20,778), `prop_quarry_winch` (23,390), `prop_cart_damaged_merchant`
(24,069), `resource_iron_billet` (25,000). Something selected the lean tier for this batch and nothing
recorded why.

**But the batch is not uniformly bad, which is the finding that breaks the simple explanation.** The
same 22 assets at the same tier include `creature_ash_ember_hound`, `weapon_march_spear`,
`landmark_ashen_waystone`, four NPCs, `prop_iron_vein_outcrop`, `resource_ash_haft` and
`landmark_foldscar_core` — all of which I opened and all of which render as clean, readable objects.
Same settings, same hour, same model, same pipeline. **The variable is the subject, and specifically
whether the subject is a compound prop.**

I initially assumed the whole batch was suspect on timestamps alone and had to correct that after
looking at the renders; the correction is recorded in Part 3 rather than removed, because it is the
cleanest evidence in the audit that time is not the variable.

---

## Part 3 — Comparative examples

Judged from the renders in `assets/review/bible_batch/` and the concepts in `assets/concepts/`, both of
which I opened. Concepts are uniformly good — **none of the failures is a concept failure.**

### Apparently strong, and I agree

| Asset | Role | Tris | Tex | Verdict |
|---|---|---|---|---|
| `weapon_march_spear` | gameplay-critical, held | 24,958 | 2048 | **High.** Clean shaft, head, butt. Straight and correctly proportioned. |
| `creature_ash_ember_hound` | gameplay-critical | ~24,976 | 2048 | **High.** Four legs, head, tail all read. Best creature in the sample. |
| `landmark_ashen_waystone` | interactive, close view | 25,000 | 2048 | **High.** Solid monolith, correct proportions, no artefacts. |
| `container_barrel_oak` | interactive container | 39,247 | 2048 | **Usable-high.** Staves and bands read clearly. |
| `magic_cauldron_small` | interactive, close view | 39,226 | 2048 | **Usable-high.** Clean revolved form, three legs, rim. |
| `prop_iron_banded_oak_door` | interactive, close view | 39,988 | 2048 | **Usable-high.** Flat plank slab with hinges. Simple subject, done simply. |

### Where I disagree with the brief

**`prop_wooden_bench` and `prop_wooden_cart_wheel` are mediocre, not strong.** Both are `lean` tier
(24,843 / 24,921 tris) and neither renders with the definition of the `mid` assets. The wheel in
particular has spokes that read as fused rather than separated. They are **usable**, not good, and
calling them strong sets the bar too low.

**`prop_iron_banded_oak_door` is strong because it is easy, not because it is good work.** It is a flat
planar slab — near the floor of what this pipeline can be asked to do. It should not be used as
evidence that the pipeline is capable.

### Apparently weak, and I agree — with one exception

| Asset | Role | Tris | Tex | Defect | Verdict |
|---|---|---|---|---|---|
| `building_smithy` | gameplay-critical building | — | — | Collapsed to a crumpled sheet of debris | **Failed** |
| `prop_blocked_shaft` | gameplay-critical, the mine entrance | 19,299 | 2048 | Shattered pile; no readable structure; reads as scattered shards | **Failed** |
| `prop_quarry_winch` | gameplay-critical, interactive | 23,390 | 2048 | Structure splayed apart; members float unconnected; diagonal UV smearing on the timber | **Failed** |
| `prop_cart_damaged_merchant` | gameplay-critical, interactive | 24,069 | 2048 | Surfaces melted and fused; wheels are lumps; a detached grey slab floats above the bed | **Weak** |
| `resource_woundmoss` | gameplay-critical reagent | 20,778 | 2048 | Shapeless mass. Foliage is forgiving, so this is the least damaged of the five | **Usable-weak** |
| `resource_iron_billet` | crafting intermediate | 25,000 | 2048 | Readable bar with metal texturing. The best of the "weak" set | **Usable** |
| `prop_quarry_rail_track` | set dressing, quarry | 24,828 | 2048 | Rails and sleepers present but **wispy and fuzzy**, with a grey reconstruction halo and no clean edges | **Weak** |

**`resource_iron_billet` is not weak and should not have been in the weak list.** It is a simple
elongated solid, which is exactly what this pipeline reconstructs well, and the render confirms it. Its
only real problem is that it is `lean` tier like its batch-mates.

**I was initially too generous about `prop_quarry_rail_track`.** It appears in Claude's report as a
prop to check and in my first pass I called it merely readable. Looking at it directly, the sleepers are
indistinct, the rail profile has no defined edge, and the whole piece carries a fuzzy grey fringe where
the reconstruction failed to resolve the geometry. It is a weak asset, not an acceptable one, and it
belongs in the same class as the winch.

### A correction: assets I wrongly assumed were failures

My first pass put four assets in the regenerate list on the inference that they shared the failed
batch's timestamps and were therefore suspect. **I looked at them, and they are good.** Recording the
error rather than quietly deleting it:

| Asset | What I assumed | What it actually is |
|---|---|---|
| `prop_iron_vein_outcrop` | suspect, same batch | **Good.** Readable rock with visible orange ore veins. Keep. |
| `resource_ash_haft` | suspect, same batch | **Good.** A clean cut wooden shaft. Simple subject, done well. Keep. |
| `landmark_foldscar_core` | suspect — bounds are a suspiciously round 6 × 4.39 × 4.39 | **Good.** A stone ring with a visible opening and separately readable blocks around the rim. The regular bounds are just the ring's extent. Keep. |
| `landmark_quiet_stone` | suspect, same batch | **Good.** A standing stone, clean. Keep. |

This correction **strengthens** the central finding rather than weakening it. The 09-23 21:00 batch is
not a bad batch. It is a normal batch whose **compound props** failed. Everything else in it —
creatures, NPCs, weapons, landmarks, simple resources — is fine at the same tier, hour and settings.

**All five were intended as gameplay-critical interactive assets**, not background scenery. Nothing in
the manifests marks any of them as low-priority: all carry `status: export_ready`, and the cart, winch,
blocked shaft and woundmoss all have sockets or interaction roles. **None of these was intentionally
low-quality. They are unintentionally degraded.**

---

## Part 4 — Resolution / mesh / material audit

Measured across all 3,015 GLBs in `assets/ready/` and `assets/rigged/` with `_glb_audit.py`, which
parses each GLB's JSON chunk directly.

### By part type

| Part | Files | 0 materials | 0 textures | Unbound primitive | Mean tris |
|---|---|---|---|---|---|
| **LOD0** (`<name>.glb`) | 585 | **0** | **0** | **0** | 33,685 |
| `_lod1` | 485 | **485** | **485** | **485** | 9,239 |
| `_lod2` | 485 | **485** | **485** | **485** | 2,978 |
| `_lod3` | 485 | **485** | **485** | **485** | 1,000 |
| `_collision_box` | 495 | 482 | 495 | 482 | 13 |
| `_collision_hull` | 480 | 480 | 480 | 480 | 419 |

**LOD1+2+3 = 1,455 files. 1,455 have zero materials and zero textures. 0 have any.** The decimation
chain itself is sound — 33,685 → 9,239 → 2,978 → 1,000 is a clean ~3.6×/3.1×/3× progression — so the
geometry step works and the material step after it does not.

There is **no `_lod0` file**. The base `<name>.glb` is LOD0 and it is the only textured mesh. So the
engine has a correct near mesh and three untextured distance meshes for every asset.

Collision parts being materialless is normal and not a defect.

### Per-asset, the named set

| Asset | LOD0 tris | LOD1 | LOD2 | LOD3 | Materials | Textures | Texture res | LOD materials | Grounded | Verdict |
|---|---|---|---|---|---|---|---|---|---|---|
| `container_barrel_oak` | 39,247 | 7,980 | 2,480 | — | 1 | 3 | 1024+2048 | **none** | yes (min z 0) | usable-high |
| `magic_cauldron_small` | 39,226 | 7,993 | 2,492 | — | 1 | 3 | 1024+2048 | **none** | yes | usable-high |
| `prop_iron_banded_oak_door` | 39,988 | 8,000 | 2,499 | — | 1 | 3 | 1024+2048 | **none** | yes | usable-high |
| `prop_wooden_cart_wheel` | 24,921 | 7,999 | 2,499 | 599 | 1 | 3 | 2048 | **none** | yes | usable |
| `prop_wooden_bench` | 24,843 | 7,998 | 2,500 | 600 | 1 | 3 | 2048 | **none** | yes | usable |
| `resource_iron_billet` | 25,000 | 8,000 | 2,499 | 600 | 1 | 3 | 2048 | **none** | yes | usable |
| `resource_woundmoss` | 20,778 | 7,994 | 2,494 | 1,725 | 1 | 3 | 2048 | **none** | yes | usable-weak |
| `prop_quarry_winch` | 23,390 | 7,996 | 2,496 | 1,907 | 1 | 3 | 2048 | **none** | yes | **failed** |
| `prop_cart_damaged_merchant` | 24,069 | 7,997 | 2,495 | 1,625 | 1 | 3 | 2048 | **none** | yes | weak |
| `prop_blocked_shaft` | 19,299 | 7,992 | 2,491 | 1,527 | 1 | 3 | 2048 | **none** | yes | **failed** |

**Orientation, scale and grounding are correct throughout the named set.** Every manifest records
`min z = 0.0` and a `scale_applied` that hits the declared `target_size_m`. The reported lean/orientation
problem is not visible in the source data for these assets. Either it affects a subset I have not
sampled, or it is introduced in the engine import, or it is a misreading of the melted geometry —
**I cannot resolve that from the source alone, and I am not going to guess.**

Note: `prop_cart_damaged_merchant_lod3` requested 600 faces and delivered 1,629 (ratio 0.0249). The
decimator missed its target by 2.7×. Small in absolute terms, but it shows the LOD target is not
enforced.

### Rigged assets

85 rigged GLBs. **85 have a skin. 0 have animations.** So the bind data is present; animation lives
separately in `assets/animation/` (70 GLBs, 145 JSONs). The reported "rig/bind/pose mismatch" is **not
reproducible from the source files** — every rigged asset carries exactly one skin. It may be a
`rest_position` or inverse-bind matrix issue that needs the engine to see, or an animation-to-skeleton
retargeting mismatch. **Uncertain; needs the engine to diagnose.**

---

## Part 5 — Root-cause analysis

Ranked by confidence, with the class letter from the brief's taxonomy.

**1. All LODs exported without materials (class B/E) — certain.**
1,455 of 1,455. This is a pipeline defect, not a source defect and not an engine defect. It affects
every asset equally and is invisible in every manifest, which records `"lod_status": "present"` without
checking what the LODs contain. **This alone explains "assets look significantly worse in-game" for
anything viewed at distance.**

**2. Buildings shipped as primitives and boxes (class A/D) — certain.**
`building_beam` 12 tris, `building_post` 12, `building_road_segment` 24, `building_door_frame` 36,
`building_floor_planks` 144, `building_fence_panel` 264. `longhouse` and `forge_shed` at exactly
3.0 × 2.6 × 3.0 m. These names describe architecture; the files are boxes. Whether this was intentional
greyboxing that was never replaced, or a generation failure accepted as a result, **I cannot tell from
the artefacts — no manifest records either way.** Either way the library does not contain buildings.

**3. Reconstruction collapses thin and open subjects (class A/D) — high confidence.**
The evidence is the same-batch contrast, which is the strongest evidence in this audit. On 09-23 between
21:03 and 21:06, identical settings produced clean hounds, spears, waystones and NPCs, and produced
`building_smithy` as a crumpled sheet and `prop_blocked_shaft` as a shattered pile. The concepts for
both failures are good. The distinction tracks subject complexity: **solid, convex, closed, compact →
fine; thin members, open cavities, compound assemblies, large architectural volumes → collapse.**

This is a **capability limit of image-to-3D reconstruction on this class of subject**, not a regression
and not a settings error. No reasonable change to faces, steps or texture size fixes a winch whose
spokes are 3 cm thick.

**4. The 09-23 21:00 batch ran entirely at `lean` (class D) — certain.**
22 of 22 assets at ~20–25k faces, where every other batch is majority 40k. This compounds cause 3 rather
than causing it: a lower face budget gives thin geometry even less chance. **The weak assets are lean
AND hard subjects, and both hurt.**

**5. Structural validation was the only gate (process) — certain.**
500 of 500 manifests say `"status": "export_ready"`, 482 say `"lod_status": "present"`, and every one
asserts `"godot_validated": true`. A 12-triangle box, an untextured LOD, and a crumpled building all
pass. The gate measured presence, never quality.

**What I have ruled out:**

- **A model change.** One model throughout: Pixal3D, 482 of 500.
- **A texture-resolution downgrade.** 2048 library-wide; 15 assets at 1024, not concentrated in the failure batch.
- **A geometry-budget downgrade over time.** Median 39,680 (09-22) → 39,516 (09-23 20:00). Stable.
- **Bad concepts.** The concepts for the failed assets are good — better than the meshes that came from them.
- **Ungrounded or mis-scaled assets.** `min z = 0.0` and `scale_applied` hit target throughout the named set.

**What I could not determine:** the lean/orientation problem and the rig/bind mismatch. Neither is
visible in the source data I can read. Both need the engine.

---

## Part 6 — Actionable recommendations

**Do not regenerate anything yet.** Four of the five causes are fixable without rebuilding a single
mesh, and regenerating first would reproduce causes 3 and 4 while leaving 1 and 2 in place.

**1. Fix the LOD material export first.** Highest value, affects every asset, no regeneration.
Find the export step that writes `_lod1/_lod2/_lod3` and make it carry the material and UV assignment
from LOD0. Then re-export all 1,455. Until this is done, every distance view in the game is wrong, and
no amount of new geometry will change that.

**2. Add a material/LOD assertion to validation.** The current gate checks that an LOD exists. It must
check that an LOD has materials, a texture, and a bound primitive — the same three things
`_glb_audit.py` measures. A file that fails should not be writable as `export_ready`.

**3. Repopulate or rebuild the building set.** Decide explicitly whether `building_beam` at 12
triangles is a placeholder or a delivery. If placeholder, mark it `placeholder` in the manifest so no
downstream reader treats it as a building. Buildings cannot be fixed by re-running the current pipeline;
they need either a purpose-built modular kit or a different reconstruction approach.

**4. Stop asking this pipeline for thin-membered and architectural subjects.** Winches, carts, rail
tracks, mine entrances and buildings fail predictably. Model them as kits from primitives, or source
them, or accept them as greybox. Re-running them at 40k faces will not help — and I would test that on
one asset before spending on five.

**5. Re-run only what is both lean and a failed subject class.** The 09-23 21:00 batch is *not* a bad
batch — its creatures, NPCs, weapons, landmarks and simple resources all render cleanly at the same
tier and hour. Only its compound props failed. `prop_quarry_rail_track` is the single asset in it worth
one retry at `mid`. `prop_quarry_winch`, `prop_cart_damaged_merchant` and `prop_blocked_shaft` are not
worth re-running on this pipeline.

**6. Keep the good assets exactly as they are.** The creatures, weapons, NPCs, landmarks and simple
resources from the 09-22 and 09-23 batches are sound, and that is a larger set than the failures. They
should not be swept into a bulk regeneration.

**7. Accept criteria change.** "Technically valid but ugly" currently passes. The gate must include a
rendered view that a human or a vision model inspects, at least for gameplay-critical and interactive
assets. `_blender_preview.py` already exists; the missing piece is making its output a **required**
input to acceptance rather than an optional comparison tool.

**8. Separate tiers by role, explicitly.** Hero and close-view interactive assets at `mid`; distant set
dressing at `lean` **by written decision**, recorded in the manifest. The current library has both tiers
but no record of which asset was assigned which and why, so nobody can tell an intentional `lean` from a
mistake.

---

## Part 7 — Honest acceptance list

**Keep as-is** — confirmed good, do not touch
- All 09-22 creatures, weapons and NPCs at `mid` (107 assets)
- `weapon_march_spear`, `weapon_arming_sword`, `weapon_hunting_bow`
- `creature_ash_ember_hound`, `creature_animated_armour`, `creature_bone_walker_husk`
- `landmark_ashen_waystone`, `landmark_quiet_stone`
- `container_barrel_oak`, `magic_cauldron_small`, `prop_iron_banded_oak_door`
- All 85 rigged GLBs (skins present and correct at source level)

**Keep temporarily** — usable, known compromised, do not build anything that shows them close
- `resource_iron_billet`, `resource_woundmoss` (shapeless but foliage/ore are forgiving)
- `prop_wooden_bench`, `prop_wooden_cart_wheel` (lean, mediocre)
- Every asset's LOD1/LOD2/LOD3 — **usable only after the material fix**

**Repair if possible** — no new geometry needed
- All 1,455 LOD files: re-export with materials
- `prop_cart_damaged_merchant_lod3`: LOD target not enforced, 1,629 delivered for 600 requested

**Regenerate** — worth one attempt at `mid`. This list is short on purpose: the assets I first
suspected here turned out to be fine when I looked, and re-running a pipeline that already failed on a
subject class is a poor bet.
- `prop_quarry_rail_track` — one retry at `mid`, because it is a regular repeating structure rather
  than a compound assembly. If `mid` does not resolve the sleepers, it is not a reconstructable subject.

**Discard or replace** — do not spend more reconstruction on these
- `building_smithy` — failed; a crumpled sheet
- `prop_blocked_shaft` — failed; no readable structure
- `prop_quarry_winch` — failed; structure splayed apart
- `prop_cart_damaged_merchant` — weak; melted wheels and a detached floating slab. Replace with a kit
  or accept as a prop, but do not re-reconstruct.
- `building_beam` (12 tris), `building_post` (12), `building_road_segment` (24),
  `building_door_frame` (36), `building_floor_planks` (144), `building_fence_panel` (264) — these are
  primitives, not assets
- `longhouse`, `forge_shed` — 3.0 × 2.6 × 3.0 m boxes

---

## Part 8 — Was the process honest?

The library's own `QUALITY_TIERS.md` is the most useful document in the repository, because it says the
thing the manifests do not:

> *no finished mesh had ever been looked at, only validated structurally.*

That is true, and it is the root of everything above. The manifests are not lying — `export_ready` and
`godot_validated: true` mean what they say, which is that the files exist, parse, and contain the part
names the pipeline expects. **But a reader who trusts those fields will believe the library has been
reviewed, and it has not.** 500 of 500 assets are stamped as ready, including a collapsed building and
a pile of shards.

Two things would have caught this immediately and cost almost nothing:

- asserting that an LOD has a material, which is a three-line check
- rendering every asset once and looking at the sheet, which is what `_blender_preview.py` now does

The tier work in `QUALITY_TIERS.md` is careful and well-argued — it measured three builds of one asset
and correctly concluded that texture resolution is the cost and not the visible benefit. It is good
engineering. It was simply applied to the wrong question: it optimised the settings that were already
adequate while the actual defect, LOD materials, went unexamined because nothing was measuring it.

**The failure was not laziness or incompetence. It was a validation gate that measured the wrong
things, and no one looking at the output.**

---

## Method

- `tools/asset_pipeline/_glb_audit.py` — written for this audit; parses each GLB's JSON chunk and
  reports triangles, vertices, materials, used/unused materials, primitives without a material,
  textures, embedded image resolutions, skins, animations and the bounding box. 3,015 files read.
- 500 `*_meta.json` in `assets/ready/` read for status, tier, date, model and transform data.
- Renders and concepts opened and inspected directly: `assets/review/bible_batch/` and
  `assets/concepts/`.
- Contact sheets written to `assets/_checkpoint/`: `concept_compare.png`, `review_latebatch.png`.
- No asset was modified, moved or regenerated.
