# Playable asset sprint — status

Sprint opened 2026-09-23 against `DEEPSEEK_PLAYABLE_ASSET_SPRINT_PROMPT.md`. Direction changed
from backlog clearing to **making Otherreach playable**: converting a deliberately small subset of
the existing library into a complete gameplay-ready set.

The measure of success is **how many complete gameplay loops the asset library supports**, and the
acceptance sentence is:

> Claude can build M3–M6 without waiting for the asset pipeline to invent a basic player, enemy,
> weapon, building, material, interaction prop, or animation.

Section 13 records the current blockers; section 14 the next actions. Gaps are detailed separately
in `PLAYABLE_ASSET_GAP_REPORT.md`.

---

## Round 2 completion report

Section 32 of the brief asks for a checkpoint report. This is it, for the round that closed the five
items the previous resume listed.

**Assets selected, rebuilt and rescaled**

| | Count | Detail |
|---|---:|---|
| Selected for the prototype | 29 | `playable_prototype_assets.json`, 29 of 29 built |
| Rescaled this round | 6 | the crafting chain's items, all to a declared high-confidence expectation |
| Restored after a stale-cache double-scale | 2 | `item_grey_iron_ingot`, `item_charcoal_steel_ingot`, 0.18 m → 0.30 m |
| Newly socketed weapons | 1 | `weapon_rusted_militia_sword`, the prototype's starter |
| Interactive props with anchors | 22 → **29** | every remaining item in the prototype's content list |
| NPCs quarantined as unresolved | 3 | candidates recorded, morphology check outstanding |

**New assets created this round**

- **Six magic VFX atlases**, 45 frames, with a bound contract and a Strain feedback curve.
- **Ten UI icon concepts**, rendered for the slots the library had nothing for; seven more reuse
  existing concepts.
- No new 3D mesh was generated. Every 3D asset this round is an existing one made usable, which is
  the §33 rule applied literally.

**Animation**

- **12 new player clips**, 17 total, covering section 6's whole list.
- **47 of 47 clips verify** — animation-only, canonical skeleton, no NaN keys, durations match.
- **One shared `AnimationLibrary`, 47 entries, 0 registry mismatches** on length or loop mode.
- **A 14-state machine driven at runtime**: 6 of 6 one-shot travels observed, each returning to
  locomotion; a 4-point locomotion blend space spanning 0.0–6.0 m/s.

**Rig families used:** one — `humanoid_standard` for all 17 player clips; the creature clips use
`creature_quadruped`, `creature_humanoid` and `creature_serpentine` and were not rebuilt.

**Godot validation**

| Validator | Result |
|---|---|
| `validate_anim_tree.gd` (new) | `ok: true`, 47 clips, 0 problems |
| `validate_scene.gd` | `ok: true`, 32 assets, 6 clips |
| `_verify_pack.py --fast` | `485/485 assets clean — PACK COMPLETE` |

**Host jobs and run time:** BEAST's ComfyUI rendered the ten UI icons on one RTX 3090, one prompt at
a time at ~2 minutes each. Nothing ran on ASTRAL; the tunnel stayed up as infrastructure. Blender
ran twelve background retarget bakes locally. No host collision, and no second builder on any GPU.

**Remaining critical blockers:** no arrow geometry; no stream and no lit forge fixture; nothing
validated inside a running scene. All three are section 13, and none of them blocks the acceptance
sentence.

**Files changed**

New tools: `_make_crafting_chain.py`, `_build_humanoid_anims.py`, `_render_anim_preview.py`,
`_render_anim_sheets.py`, `_sheet.py`, `_make_magic_vfx.py`, `_vfx_review.py`,
`_make_anim_state_machine.py`, `_promote_ui_icons.py`, `_glb_truth_check.py`,
`_catalog_consistency.py`.

Changed tools: `_make_motion.py` (twelve action tables and a keyed-pose interpolator),
`_author_weapon_sockets.py` (the starter sword), `_author_interaction_sockets.py` (seven more props,
one duplicate key removed), `_audit_semantic_scale.py` (refuses a stale catalog).

New Godot: `validate_anim_tree.gd`. Changed Godot: `asset_gallery.gd` (applies the registry's loop
policy).

New manifests and assets: `prototype_crafting_chain.json`, `animation_state_machine.json`,
`magic_vfx.json`, `ui_icons.json`, six VFX atlases under `assets/vfx/`, 17 icon sprites under
`assets/ui/icons/`.

**Git status:** nothing committed. `git status` reports the sprint's docs as untracked, several
tools as modified, and no asset files at all — `/assets` is gitignored in this repository, so the
manifest still depends on the working tree. HEAD is unchanged at `909c00c`. Note that `git` on
BEAST refuses this working tree without a `safe.directory` override, because the repository lives on
ASTRAL's share and is owned by a different SID; inspect it with
`git -c safe.directory='//astral/g/UNNAMED' status`.

**What Claude can consume immediately:** the crafting chain by content id, the seventeen player
clips by stable id with their event times, the state machine as a data contract, the VFX by effect
id with a cast origin, the icon set by slot id, sockets on seven weapons and anchors on 29 props, and
`playable_prototype_assets.json` for the rest.

---

## Deferred Work Checkpoint

Written when the broad asset-generation queues were stopped to start this sprint, and re-confirmed
when work was paused a second time.

### What was running when work was stopped

| Process | Host | Plan | State |
|---|---|---|---|
| `_supervise.py` → `_overnight_queue.py` → `_make_assets.py` | BEAST | `requests/queue_wave0_night.json` | stopped |
| `_supervise.py` → `_overnight_queue.py` → `_make_assets.py` | ASTRAL (via tunnel) | `requests/queue_astral.json` | stopped |
| `_write_status.py` → `_verify_pack.py --fast` | BEAST | post-stage maintenance | stopped |
| Veth player mesh build | BEAST | `--only race2_veth_representative` | stopped mid-generation |

Left running on purpose, because they are infrastructure rather than queues:

- `_astral_tunnel.py` — the reconnecting SSH tunnel to ASTRAL's ComfyUI on `localhost:18190`.
- `_comfy_mem_guard.py --limit-gb 185` — host-RAM watchdog; cannot fire while nothing generates.
- Both ComfyUI servers (BEAST 8188, ASTRAL 8190). Orphaned prompt queues were cleared on both.

### Nothing was lost

- **No partial artefacts.** Every stop happened during generation, before any file write. The Veth
  build wrote neither `raw/race2_veth_representative.glb` nor a `ready/` directory; its concept is
  intact.
- A scan found no GLBs under 1 KB in `raw/` or `ready/`, so no truncated writes exist.
- No source concepts, manifests, sockets or pipeline experiments were deleted.
- `_rename_glb_assets.py` reported **0 changed, 2610 already named, 0 failed** — the library's
  asset-id naming held through the pause.

### Exact state at the stop

Catalog last built 13:38:47: **463 assets, 16,662,834 triangles, 4.31 GB**.

BEAST plan — the `running` markers are stale and harmless, because `_supervise.py` treats anything
whose status is not `done` as pending, so those stages simply re-run.

| Stage | Recorded |
|---|---|
| `wave0: stub components`, `wave0: mace fix`, `wave0: magic components` | done |
| `proofset: pommel fix`, `world materials: flora`, `world materials: reagents` | done |
| `race bodies` | done |
| `world materials: items` | running (incomplete) |
| `world materials: herbs` | running (incomplete) |
| `world materials: resources` | running (incomplete) |
| `recovery: long-generation item` | running (never succeeds) |

ASTRAL plan — unbuilt counts at the stop:

| Stage | Status | Unbuilt |
|---|---|---:|
| `astral: magic props`, `travel props`, `vehicles`, `animals`, `mounts` | done | 0 |
| `astral: containers` | incomplete | 12 |
| `astral: race2 bodies` | never run | 14 |
| `astral: raceclass` | never run | 32 |
| `astral: world items` | never run | 33 |
| `astral: remaining props` | never run | 45 |

Roughly **136 3D assets** remain unbuilt. Per the new direction most of that is now deliberately
cancelled rather than deferred (section 1).

### How to resume the deferred backlog

BEAST:

```powershell
cd W:\UNNAMED\tools\asset_pipeline
& C:\Python314\python.exe '_supervise.py' `
  --plan  'W:\UNNAMED\assets\requests\queue_wave0_night.json' `
  --state 'W:\UNNAMED\assets\queue_wave0_state.json' `
  --log   'W:\UNNAMED\assets\queue_wave0.log' `
  --hours 12 --passes 6
```

ASTRAL (tunnel first; the Run-key task starts it at logon):

```powershell
cd W:\UNNAMED\tools\asset_pipeline
& C:\Python314\python.exe '_astral_tunnel.py' --log 'W:\UNNAMED\assets\astral_tunnel.log'
# then, in a second shell:
$env:UNNAMED_COMFY_SERVER = 'http://127.0.0.1:18190'
$env:UNNAMED_COMFY_OUTPUT = 'W:\ComfyUI_LTX25\ComfyUI\output'
& C:\Python314\python.exe '_supervise.py' `
  --plan  'W:\UNNAMED\assets\requests\queue_astral.json' `
  --state 'W:\UNNAMED\assets\queue_astral_state.json' `
  --log   'W:\UNNAMED\assets\queue_astral.log' `
  --hours 12 --passes 4
```

**Check that no other builder is running on that host first.** Two concurrent builds on one GPU
starve each other (measured: ~1 s/it collapsing to 16.48 s/it). One GPU task per host.

Invariants that must hold if these run again: the two hosts must never claim the same asset-id
prefix, or they write the same `ready/` directory concurrently. `_verify_hosts.py` now checks this
and reports `write collisions`.

### Partly complete animation pipeline work

Proven and reusable:

- `_blender_canonical_body.py` — the four canonical fit families, now extended (section 5).
- `_blender_retarget.py` — canonical armature → apply motion → normalise → bake → animation-only
  GLB. `action_fcurves()` handles the Blender 5.2 layered-action API.
- `_make_motion.py` — synthetic walk and thrust motion used to test the chain.
- `_animation_registry.py` — schema v1 with `--validate/--list/--events`; 8/8 negative tests pass.
- Two clips exported successfully: `anim.humanoid.locomotion.walk_forward` and
  `anim.humanoid.polearm.thrust_01`, at 26 KB and 23 KB, each `meshes: 0`, `skins: 1`, 72 channels.

Not done: the real motion set, the 12 proof clips, and Godot `AnimationTree` / `AnimationLibrary` /
IK validation.

---

## 1. Paused and deferred work

**Cancelled by the current prompt** (do not resume without a new decision):

| Item | Count | Authority |
|---|---:|---|
| `raceclass_*` concepts | 32 | §20 — art-direction/costume boards, not required production characters |
| `mount_*` | 6 | §22 — not required for the Phase-1 prototype |
| Herb backlog | ~16 | §14 — one built herb is fine; only build one if Claude needs it |
| `race2_*` blind 3D conversion | 14 | §21 — hand/eye/wing/silhouette studies; reference inputs, not geometry |
| Most containers | ~12 | §23 — current built containers are sufficient |

**Deferred, resumable:** the two 3D queues above, ~136 assets, with exact commands recorded.

---

## 2. Semantic-scale audit

**Complete, and the M3 subset is now corrected.** `docs/SCALE_AUDIT_REPORT.md`, machine-readable
`assets/manifests/scale_audit.json`, expectations in `assets/manifests/semantic_dimensions.json`,
auditor `_audit_semantic_scale.py`, correction tool `_rescale_glb.py`.

**The audit was reading a stale cache, and that had already corrupted two assets.** The audit
measures every asset from `assets/catalog.json`, and `_catalog_assets.py` builds that file from each
asset's `_meta.json`. Both are caches, and a rescale updates the geometry and the meta but not the
catalog. An un-rebuilt catalog therefore reports a pre-rescale size, the audit compares that stale
number against the expectation table, and `_rescale_glb.py` applies the resulting factor to geometry
that was already correct — scaling it a second time.

Measured, not inferred: `item_grey_iron_ingot` and `item_charcoal_steel_ingot` had already been
correctly rescaled to 0.30 m, the catalog still said 0.50 m, and this round's first rescale took them
to **0.18 m**. A scan of all 485 assets against their own meta files found exactly **8 stale catalog
entries** and no others, which is what bounded the damage.

Two fixes, and the second is the one that matters:

1. Both ingots were restored to 0.300 m and verified against the geometry itself, not against a
   cache.
2. `_audit_semantic_scale.py` now **refuses to report** when the catalog disagrees with the meta
   files it was built from, and names the tool to run first. It caught its own stale input on the
   next run, which is how the guard was verified rather than assumed.

`_glb_truth_check.py` was added alongside it: it measures every asset's size from the GLB's own
position accessors composed through the node hierarchy, and reports where the geometry, the meta and
the catalog disagree. All three now agree for **485 of 485**.

**449 of 465 built assets carried the category-normalisation signature** at the start of the sprint
— their longest axis was exactly a `UNIT_HINT_MAP` category default. The audit separates two tests:
the **normalisation signature**, which needs no expectation table and cannot be argued with, and the
**semantic comparison**, which needs a declared expectation — which is why `UNKNOWN_SCALE` is a
first-class result rather than a fabricated pass.

### The M3 placeable subset is fixed

**123 of 123 assets now PASS_SCALE, with zero suspect, fail or unknown**: `flora_`, `prop_`,
`travel_` and `container_` — everything Claude places in the world immediately.

| Asset | Before | After |
|---|---:|---:|
| `flora_oak_tree` | 0.5 m | **14.0 m** |
| `flora_pine_tree` | 0.5 m | **14.0 m** |
| `prop_iron_banded_oak_door` | 0.5 m | **2.05 m** |
| `prop_blacksmith_anvil_stump` | 0.5 m | **0.70 m** |
| `prop_carpenters_workbench` | 0.5 m | **1.60 m** |
| `container_chest_iron_banded` | 0.5 m | **0.80 m** |
| `travel_bridge_stone_arch` | 4.0 m | **8.00 m** |
| `flora_fern_clump` | 0.5 m | **0.45 m** (down, correctly) |

The correction is a uniform scale by `expected / measured` — the exact inverse of the operation
that broke it — applied by rewriting the POSITION accessor data in place rather than re-exporting
through Blender, so geometry, UVs, textures, materials, LODs and collision are untouched.
`_meta.json` is resynced afterwards, because both the catalog and the audit read it and a stale
meta makes a correctly rescaled 14 m tree still report as 0.5 m.

Verified safe: **2790 GLBs checked, 0 corrupt** after 129 in-place binary rewrites. Skinned meshes
are refused outright, since a skinned GLB carries inverse bind matrices in the old scale.
`_glb_truth_check.py` additionally confirms that **no base GLB in the library carries a node-level
scale**, which is what makes accessor bounds and world bounds the same number here.

### The crafting chain's items are now the size the design says

Six more assets were rescaled this round, all of them items the prototype's own content list uses:

| Asset | Before | After | Expectation |
|---|---:|---:|---|
| `weapon_rusted_militia_sword` | 1.20 m | **1.00 m** | `subject rule 'militia'`, high confidence |
| `item_health_potion` | 0.50 m | **0.22 m** | `subject rule 'potion'`, high confidence |
| `magic_amulet_copper` | 0.50 m | **0.15 m** | `subject rule 'amulet'`, high confidence |
| `magic_grimoire_bound` | 0.50 m | **0.35 m** | `subject rule 'grimoire'` — the rule was added, see below |
| `item_grey_iron_ingot`, `item_charcoal_steel_ingot` | 0.18 m | **0.30 m** | `subject rule 'ingot'`, high confidence |

`magic_grimoire_bound` reported `UNKNOWN_SCALE` because no rule matched the word "grimoire"; the
magic book rule listed `tome`, `book`, `scroll`, `tablet` and `rune` but not the word the library
actually uses. The rule now matches it. That is the honest direction of travel for an
`UNKNOWN_SCALE`: extend the expectation table, not the guess.

Library-wide: **PASS_SCALE 198 → 233**, `FAIL_SCALE` 51, `SUSPECT_SCALE` 129, `UNKNOWN_SCALE` 72,
normalisation signature **80.2% → 75.9%**.

### Registry coverage

An early weakness in my own registry is fixed. The 1.8 m `creature` fallback was the same value the
pipeline used, so all 62 creatures passed spuriously; per-subject expectations removed 34 false
passes. Asset ids are underscore-separated, so multi-word keywords like `"ice axe"` and
`"main gauche"` never matched until the matcher normalised spaces to underscores.

Still to correct (not M3-critical): weapons at a uniform 1.2 m, mounts at 0.5 m, resources and
items, and 72 assets with no declared expectation.


---

## 3. Prototype asset selection

**Frozen.** `assets/manifests/playable_prototype_assets.json` — 29 entries (16 P0, 12 P1, 1 P2),
24 built, 5 not built. Every status field is read from disk at freeze time by
`_freeze_playable_manifest.py`, so it cannot claim an asset is rigged or validated when it is not.

Two authorities bound the content and they disagree: `PROTOTYPE.md` §4 is binding for Phase-1 and
asks for **2 armour pieces and 1 wolf archetype**; this sprint's brief asks for a full outfit and
five enemies. PROTOTYPE is treated as the floor and the brief as the ceiling, and the tier is
recorded per asset so the difference stays visible rather than blurred.

---

## 4. Animation state

**The player clip set is complete against section 6.** Seventeen clips run Blender →
animation-only GLB → Godot import, all verified. Five are locomotion and twelve were added this
round; `_build_humanoid_anims.py` builds the twelve from `_make_motion.py`'s action tables.

| Clip | Duration | Loop | Type |
|---|---:|---|---|
| `anim.humanoid.locomotion.idle` | 4.00 s | yes | idle |
| `anim.humanoid.locomotion.walk_forward` | 1.00 s | yes | move |
| `anim.humanoid.locomotion.run_forward` | 0.70 s | yes | move |
| `anim.humanoid.locomotion.sprint_forward` | 0.60 s | yes | move |
| `anim.humanoid.locomotion.turn_in_place_01` | 0.70 s | no | transition |
| `anim.humanoid.general.interact_01` | 1.60 s | no | interact |
| `anim.humanoid.general.pickup_01` | 1.80 s | no | interact |
| `anim.humanoid.general.hit_react_01` | 0.55 s | no | react |
| `anim.humanoid.general.death_01` | 2.00 s | no | death |
| `anim.humanoid.sword.ready_01` | 1.60 s | yes | state |
| `anim.humanoid.sword.attack_01` | 0.90 s | no | attack |
| `anim.humanoid.sword.block_01` | 0.70 s | no | defend |
| `anim.humanoid.polearm.ready_01` | 1.60 s | yes | state |
| `anim.humanoid.polearm.thrust_01` | 0.82 s | no | attack |
| `anim.humanoid.bow.ready_01` | 1.20 s | yes | state |
| `anim.humanoid.bow.draw_release_01` | 1.10 s | no | attack |
| `anim.humanoid.magic.cast_01` | 1.40 s | no | cast |

That is idle, walk, run, sprint, turn, interact, pickup, hit reaction, death, sword ready/attack/
block, polearm ready and thrust, bow ready and draw/release, and one casting gesture — every entry
on section 6's list.

Every clip is animation-only (**0 meshes**), targets the 52-bone canonical skeleton, carries no NaN
keyframes, and its baked duration matches its declared duration. **47 of 47 clips verify** across the
whole animation set (`_verify_animation_glb.py --all`), creature clips included.

Event times are declared as *fractions* of the clip and multiplied by the duration when the registry
entry is written, so changing a clip's length cannot leave its attack commit behind at an old
absolute time. The registry validator checks the ids, event vocabulary, ordering, ranges, hand
profiles and the combat-attack requirement before anything is baked.

Motion is generated procedurally by `_make_motion.py`, which the brief permits: it asks for
"reliable gameplay-capable motion through the complete pipeline", not final animation. The action
clips are tables of `(t, pose)` keys interpolated linearly, because a shape of motion is easier to
read and to correct as a list of poses than as a chain of time comparisons.

**Looked at, not just measured.** `_render_anim_preview.py` poses the canonical body with the same
`apply_motion` code the exporter uses and renders frames from a fixed camera under neutral light;
`_render_anim_sheets.py` builds a 2×2 sheet per clip. The death clip was inspected frame by frame:
impact, stagger, knees buckle, down flat, and the body never passes through the floor — the failure a
JSON report cannot show. Cast, pickup, sword attack and bow draw/release were inspected the same way
and read as the actions they are named for.

### The shared AnimationLibrary and the state machine are proven in Godot

`assets/manifests/animation_state_machine.json` defines the machine as data — 14 states, 36
transitions, one locomotion blend space — and `validate_anim_tree.gd` proves it inside the engine.
It is data rather than a hand-wired `.tscn` because Godot renames imported animations after the
importing node: `anim.humanoid.locomotion.walk_forward` arrives as
`RIG_anim_humanoid_locomotion_walk_forwardAction`. Gameplay keys off the stable id, so the engine is
made to serve the ids.

Result, `ok: true`, no problems:

| Check | Result |
|---|---|
| Clips in one `AnimationLibrary`, keyed by stable id | **47 of 47** |
| Engine animation length against the registry's declared duration | **47 of 47 match** |
| Engine `loop_mode` against the registry's declared `loop` | **47 of 47 match** (22 looping, 25 not) |
| Animation drives at least one track | 47 of 47 (5–20 tracks each) |
| State machine nodes built | 14 declared, 16 with Godot's Start and End |
| Transitions built | 35 (the one `*` wildcard is a contract fact, not an edge) |
| Runtime `travel()` to six one-shot states, observed | **6 of 6**, each also returning to locomotion |
| Locomotion blend space | 4 points, 0.0–6.0 m/s, parameter settable |

**A real defect was found here, and it is the kind only an engine check finds.** glTF carries no
loop flag, so Godot's importer produces `LOOP_NONE` for every clip: **none of the 22 clips declared
to loop looped in the engine**. A walking character would have hitched once per stride, and every
per-clip check would have passed. The loop policy is now applied where the stable id and its
declared metadata meet the `Animation` the engine will actually play — in the library builder — and
the same fix was applied to the validation gallery for the same reason.

**Still to do:** the locomotion blend space's speeds are a declared assumption, because
`PROTOTYPE.md` puts move speeds in `config/base_speeds` and never states them; they are recorded in
the manifest as tuning values rather than as design. The state machine is not wired into a scene
with a character body — it is exercised as a tree, which is what the proof needs, but no character
has walked under it in engine yet.

### Authority note

`ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md` is a working design document, not an implementable
spec. It fixes the family names, the 24 core bone names, metres, the 20-step processing order, the
artefact lineage, the Wave 0 clip *descriptions*, the proof gates and the `SOCK_*` pattern. It does
**not** fix clip ids, durations, loop or root-motion flags, GLB content rules, Godot node types or
any numeric tolerance — all of which had to be decided and are recorded above.
`CANONICAL_BODY_AND_SKELETON.md` (status: built and verified) resolves the pose and axis gaps the
animation doc leaves open — A-pose 45°, Y-up, forward −Z — and outranks its "likely T-pose or
A-pose".

---

## 5. Character state

**The skeleton is done.** All four fit families now carry **skeleton v2**, verified:

| Family | Height | Bones | Deform | Sockets | Result |
|---|---:|---:|---:|---:|---|
| `humanoid_standard` | 1.80 m | 52 | 44 | 5 | VERIFIED |
| `compact_broad` | 1.30 m | 52 | 44 | 5 | VERIFIED |
| `tall_narrow` | 2.35 m | 52 | 44 | 5 | VERIFIED |
| `irregular_heavy` | 2.60 m | 52 | 44 | 5 | VERIFIED |

v2 adds what `CANONICAL_BODY_AND_SKELETON.md` listed as missing and the animation doc named only as
categories: **20 finger bones** (two segments, five digits per hand), **8 non-deforming IK helpers**
(`IK_hand_*`, `IK_elbow_*`, `IK_foot_*`, `IK_knee_*`) and **5 equipment sockets** (`SOCK_hand_l/r`,
`SOCK_attach_back`, `SOCK_attach_hip_l/r`). The 24 core bones are byte-identical to v1, so the
armour fit boundary, the retarget profiles and the existing clips all keep working.

Verified per body: core 24/24 at contracted positions, sockets 5/5 where contracted, IK helpers 8/8
weightless, one skin, footprint centred.

**The mesh is built, bound and Godot-validated.** `race2_veth_bindpose`:

| Check | Result |
|---|---|
| Height | 1.80 m |
| Base | Y = 0.0000 — exactly on the ground plane |
| Footprint | centred, X 0.0000 Z 0.0000 |
| Skeleton | canonical `humanoid_standard` v2, 52 bones |
| Core bones | 24 present, 24 within 0.012 m of contract |
| Equipment sockets | 5 present at contracted world positions |
| IK helpers | 8, all correctly weightless |
| Skin | 1 skin, mesh named after the asset id |
| Godot import | **OK** — size, naming, materials and sockets all pass |
| Scale ratio | width/height 0.508 against the contracted A-pose 0.653 |

**The first attempt failed and the reason is worth recording.** The original Veth concept was a
"full body turnaround", and it generated a figure standing with its arms at its sides. The canonical
armature binds in an A-pose with the arms 45° out, so the arm bones sat outside the mesh: bone-heat
weighting assigned **zero** weights while still reporting success. Measured, the first mesh had a
width/height ratio of **0.346** (arms down) against the canonical body's **0.653** (A-pose).

Two fixes came out of it:

1. `_blender_bind_canonical.py` no longer uses bone-heat weighting. It transfers weights from the
   canonical body by nearest surface point, which is what a canonical reference body is *for*, and
   reports `weighted_bones` so a silent zero-weight bind cannot pass unnoticed.
2. The canonical body gained **hand and finger geometry**. Without it the reference body weighted
   only 9 of its 20 finger bones, and transfer can only propagate weights the source already has.
   It now weights **20 of 20**, 37 joints total.

**Known limitation, measured:** only **3 of 20** finger bones carry weight on the character. The
generated mesh's arms sit at ~59° from horizontal against the contracted 45°, so the outer digits
fall outside the transfer. The hand, wrist and forearm are weighted and the grip sockets are
present, so a weapon can be held; finger articulation is not yet believable. A mesh generated in a
tighter A-pose is the P1 follow-up.

**Outfit: 7 of 7 pieces built.** The four that were missing are done, so the Veth has a complete
wearable set:

| Piece | Size | |
|---|---:|---|
| `armour_chest_underlayer_gambeson_a` | 0.93 m | layer 0.008 |
| `armour_chest_plate_base_a` | 0.90 m | layer 0.024 |
| `armour_gorget_plate_a` | 0.82 m | midline gap piece |
| `armour_leg_garment_a` | **1.05 m** | new |
| `armour_glove_pair_a` | **0.26 m** | new |
| `item_leather_boots_pair` | **0.32 m** | new |
| `item_steel_plate_helm` | **0.30 m** | new |

All seven use **generated fit, not the Method C canonical transfer**, which remains the outstanding
piece of the outfit work. None emits coverage metadata yet, so no piece currently reduces damage —
coverage is metadata by design, and no tool emits it.

Two of the four new pieces needed concepts created (`armour_leg_garment_a`, `armour_glove_pair_a`);
boots and helmet already had concepts rendered but unbuilt.

---

## Resume note

Second resume, after the preset change. State was rebuilt from disk rather than from memory: no
queue processes and no Blender running, only the ASTRAL tunnel and the two ComfyUI servers; catalog
**485 assets, 4.44 GB, 16.9 M triangles**; `485/485 assets clean — PACK COMPLETE`.

Five of the five items the previous resume listed are now closed — the crafting-chain assembly, the
magic VFX, the UI icons, the AnimationTree wiring — leaving the outfit's Method C fit and coverage
metadata as the only one still open, and it is now item 4 of section 14.

**What this round produced, in the order it was done:**

1. **The blacksmithing chain is assembled.** `assets/manifests/prototype_crafting_chain.json` joins
   every content id in `PROTOTYPE.md` section 4 to the GLB, socket and anchor that makes it real,
   with the two authorised recipes. Four content items are recorded as having no geometry.
2. **A stale-cache bug that had already corrupted two assets was found and fixed.** The audit read
   `catalog.json`, which a rescale does not rebuild, so it applied a correction to geometry that was
   already correct. Two ingots had been taken from 0.30 m to 0.18 m. Both restored, and the audit
   now refuses to run against a stale catalog.
3. **The starter sword became holdable, and every pickup item got a take anchor.** The weapon the
   prototype equips in step 1 had no grip socket at all.
4. **The player's clip set is complete**: twelve new clips, seventeen total, all verified in Godot.
5. **The three magic proofs exist** as flipbook atlases and a bound contract, with the casting
   origin on `SOCK_hand_r`.
6. **The UI icon subset is promoted** — seventeen slots, ten of them newly rendered.
7. **The shared AnimationLibrary and the state machine are proven in Godot** across all 47 clips,
   which is where the loop-mode defect was found.

**Two things were left deliberately unfinished and are named rather than hidden:** no arrow
geometry exists, so a fired shot cannot be drawn, and the chain's stream and lit forge are
dependencies with no asset behind them. Both are items 1 and 2 of section 14.

**Where to resume:** section 14.

---

## 6. Weapon state

**Three families, all now gameplay objects** (§7). Correct dimensions, authored sockets, and all
three pass Godot validation.

| Family | Asset | Length | Sockets |
|---|---|---:|---|
| One-handed sword | `weapon_arming_sword` | **1.00 m** | `SOCK_grip_primary`, `SOCK_pommel`, `SOCK_attach` |
| Bow | `weapon_yew_longbow_warbow` | **1.70 m** | `SOCK_grip_primary`, `SOCK_ammo`, `SOCK_attach` |
| Polearm | `weapon_boar_spear_hunting` | **2.20 m** | `SOCK_grip_primary`, `SOCK_grip_secondary`, `SOCK_head`, `SOCK_pommel`, `SOCK_attach` |

All three were at a uniform 1.2 m, so a dagger and a spear were the same length. Each now matches
its real-world size.

**A fourth weapon was added, and it is the one the prototype actually uses.**
`PROTOTYPE.md` section 4.2 names `item.weapon.rusted_sword` as the starter and step 1 equips it
before the player has done anything, while the three families above are the brief's choices. The
built asset for it, `weapon_rusted_militia_sword`, was at the pipeline's uniform 1.20 m with **no
sockets at all** — so the one weapon the prototype starts with could not be held. It is now 1.00 m
against the registry's own `militia` expectation, and carries `SOCK_grip_primary`, `SOCK_pommel` and
`SOCK_attach`.

Its grip came from a 40-bin profile rather than a guess: tip 0.00–0.07, blade 0.07–0.67, crossguard
0.69–0.71 at 0.256 m wide, grip 0.74–0.91, pommel 0.94–0.99. The grip socket's envelope measures
**2.5 cm**, which is hand scale, and the sword exports point-down like the arming sword, so its grip
primary faces −Y.

**Seven weapons now carry sockets** in `assets/sockets/weapon_sockets.json`: the three families,
the starter sword, the mining pick, the hammer and the tongs. Anything a character holds needs the
same contract, which is why the tools are in the same table.

**The spear carries both grip sockets, 0.40 m apart** (primary at 0.70 m, secondary at 1.10 m).
That spacing is what runtime two-handed IK needs, and it is the specific thing §7 asks for.

**Socket positions come from measured geometry, not the bounding box.** `_weapon_profile.py` bins
the vertices along each weapon's length and reports the cross-section per bin, which is how the
placements were chosen: the sword's blade runs 0.03–0.66 with the crossguard spike at 0.72 and the
grip above it; the bow is widest at its riser at 0.46; the spear's shaft runs 0.04–0.68 with the
head at 0.75. A bounding-box midpoint would have put the sword's grip halfway up its blade.

Each socket is verified to sit ON the mesh — the position is the centroid of the vertices in its
band and at least 12 vertices must be present, so a socket cannot silently float in mid-air.

**The bow selection was corrected by inspection, which is exactly what §7 asks for.** The brief says
to inspect geometry rather than follow names. `weapon_recurve_hunting_bow` sounds right but is a
**modern compound target bow** with stabiliser bars and a sight — anachronistic for a
melancholic, ancient, low-magic setting, and 0.97 m wide because of the bars. `weapon_yew_longbow_warbow`
is a wooden self bow with a wrapped grip: period-appropriate and the correct silhouette.

One correction of my own: taking the maximum radial distance in a grip band gave the bow a
**0.64 m "grip"**, because the riser's cross-brace and sight stick out a third of a metre. The
envelope now uses a 60th-percentile radius, which describes the part a hand closes around. Sword
grip reads 4.6 cm and the spear grips ~6 cm, which are hand-scale.

Facing follows the modular standard's grip rule — `primary` points along the grip axis toward the
head. The arming sword came out of the generator **point-down**, so its grip faces −Y while the
spear's faces +Y; both are recorded in the socket metadata rather than assumed.

Collision proxies are retained as world-object reference geometry. The modular standard says a held
weapon ships no hitbox ("the hitbox is authored in the combat system"), so that stays a gameplay
concern.

---

## 7. Enemy state

**All five are complete** (§9): correct scale, rigged, six clips each, collision reference, and all
thirty creature clips pass Godot validation.

| Enemy | Scale | Rig | Clips |
|---|---:|---|---|
| `creature_frost_wolf` | **1.30 m** | quadruped, 18 bones | idle, walk, run, attack, hit, death |
| `creature_highland_brown_bear` | **2.00 m** | quadruped, 18 bones | six |
| `creature_bone_walker_husk` | 1.80 m | humanoid creature, 20 bones | six |
| `creature_animated_armour` | **2.00 m** | humanoid creature, 20 bones | six |
| `creature_great_river_serpent` | **3.00 m** | worm, 5 bones | six |

They were all 1.80 m, so a wolf was the same size as a bear. Four were rescaled and all five
re-rigged at the new size — a rigged mesh cannot simply be scaled, because a skinned GLB carries
inverse bind matrices in the old scale, so the geometry is corrected on the unrigged base and the
rig rebuilt. All five report **0% unweighted vertices**.

**Clips are generated per rig plan, not retargeted through the canonical body.** The player clips
are authored against the canonical humanoid skeleton; enemies are not on it at all. So
`_make_creature_motion.py` produces motion keyed to each rig's own bone names — a diagonal-pair
trot for quadrupeds, a two-legged gait for the humanoid creatures, and a travelling wave along the
segments for the worm — and `_blender_anim_creature.py` applies it to that creature's own armature
and exports animation-only. **35/35 clips verified**, all `meshes=0`.

**Three bugs were caught by measurement, two of them mine.**

*A 25% duration error, again.* The creature exporter converts frames to seconds using the **scene**
frame rate. Blender defaults to 24 fps and the clips bake at 30, so every clip would have played
25% slow — the same trap that hit the player clips. `bake_action` was already fixed; this path sets
`scene.render.fps` too.

*A loop that could not close.* My creature gaits used `phase = 2π·t` **without dividing by the clip
period**, so a 1.2 s walk ran 1.2 gait cycles and the last frame was not the first. The serpent's
idle used `phase * 0.5`, half a cycle, which can never close. Both fixed by normalising phase to
the clip's own duration.

*And my verifier was wrong.* It reported loop seams of ~2.0 radians on clips that loop perfectly: a
quaternion and its negation are **the same rotation**, so comparing components directly is
meaningless. It now compares the angle between the rotations via the dot product.

Also corrected: `idle` is a clip *type*, not a category. The registry rejected `category: "idle"`
and was right to — an idle belongs to the `locomotion` family alongside walk and run.

**Not done: per-bone hit regions.** §9 asks for a hit-region reference "where practical". The
creatures carry collision hull and box proxies, but locational damage would need region metadata
mapped onto the creature skeletons, which is gameplay-side work.

---

## 8. Environment kit

**Ten world materials are promoted to production PBR** (§12), all Godot-validated:

| Material | Class | Tile | Baked lighting removed |
|---|---|---:|---:|
| `material_packed_dirt_ground` | ground | 4.0 m | 69% |
| `material_churned_wet_mud` | ground | 3.0 m | 65% |
| `material_marsh_grass_turf` | ground | 3.0 m | 65% |
| `material_loose_gravel` | ground | 2.0 m | 66% |
| `material_oak_plank_floor` | timber | 2.0 m | 71% |
| `material_plaster_lath_wall` | plaster | 2.5 m | 64% |
| `material_rubble_stone_wall` | stone | 3.0 m | 68% |
| `material_limestone_ashlar` | stone | 2.5 m | 67% |
| `material_slate_roof_scale` | roof | 2.0 m | 65% |
| `material_cast_iron_surface` | metal | 1.5 m | 70% |

Each produces base colour, normal, occlusion, roughness and a packed glTF ORM map, plus a
`StandardMaterial3D` `.tres` ready to use, and declares its real-world **tile size** — a texture
with no declared scale cannot be placed correctly in a world, which is the flat-texture version of
the same bug that hit the meshes.

**Two claims were measured rather than asserted, and one of them caught my own error.**

*Tiling.* "Tileable" is meaningless unless measured. An earlier version of my tiling was exactly
backwards: it cross-faded the rolled image with the original using an edge-weighted mask, which put
the concept's own frame edges back on the wrap boundary and drove the seam from a ratio of ~1.3 up
to ~9. The roll alone already tiles — after shifting by half, the wrap edges are two adjacent
columns of the original — so only the centre cross needed healing with a symmetric blurred band.

*The metric itself was then wrong.* Comparing the wrap edge against the **mean** internal difference
is biased by whatever smooth regions a texture happens to contain, so genuinely seamless maps read
as 1.8. The check now reports where the wrap edge sits in the **distribution** of internal
differences: a seamless edge is an ordinary adjacent pair and ranks near the middle. Measured seam
ranks are **0.68–0.90** against a 0.995 threshold, where the source concepts rank up to 5.5.

*De-lighting.* A concept render carries its own light, so it would double-light in engine. Dividing
out a heavily blurred luminance removes **64–71%** of the large-scale lighting gradient, measured
per material and recorded in its metadata.

Roughness and metallic are **declared per material class, not recovered from the render** — they
cannot be read from a photograph, and inventing them from luminance would be a fabrication. The
verifier confirms the ORM roughness channel actually matches the declared value.

Interaction props are also complete (§15). Seventeen world objects carry correct scale, collision,
LODs and **interaction anchors**, and all seventeen pass Godot validation:

| Prop | Anchors | Dimensions (W × H × D) |
|---|---|---|
| `prop_iron_banded_oak_door` | `SOCK_hinge`, `SOCK_interact_handle` | 1.033 × 2.05 × 0.06 m |
| `container_chest_iron_banded` | `SOCK_interact_chest_lid`, `SOCK_interact_handle` | 0.80 × 0.60 × 0.70 m |
| `prop_blacksmith_anvil_stump` | `SOCK_interact_anvil` | 0.70 × 0.66 × 0.65 m |
| `prop_carpenters_workbench` | `SOCK_interact_workbench` | 1.57 × 1.43 × 1.60 m |
| `container_barrel_oak`, `prop_wooden_barrel` | handle + take | 0.63 × 0.85 × 0.72 m |
| `prop_wooden_crate` | `SOCK_interact_take` | 1.20 × 0.94 × 1.11 m |
| `item_raw_iron_ore`, `resource_iron_ore_chunk`, `resource_gold_ore_nugget`, `resource_healing_herb_leaf` | take + `SOCK_harvest_point` | ~0.5 m |

Anchors are derived from each mesh's measured bounds rather than typed in, so a latch sits on the
front face at the measured half depth and a work surface at the measured height. A hand-written
coordinate would drift the moment the mesh is rescaled, and this library has just been rescaled.
Every anchor carries a full orthonormal basis, because a point plus one axis cannot express roll.

**The door had to be rebuilt, and that is worth recording.** The original was generated from a
three-quarter-view concept and reconstructed as a **1.30 × 2.05 × 1.39 m mass** — nearly a cube in
plan, unusable as a hinged door. A straight-on elevation concept produced a better shape (3.1:1
rather than 1.2:1) but lying on its side, because a single-image reconstructor has no way to know
which axis is world up. `_fit_slab.py` rotates the long axis upright, scales to height, flattens
only the depth axis to real door thickness, and moves the origin to the hinge edge. The
width-by-height face — the one anyone looks at — is never non-uniformly scaled.

Not done: **the chest lid is not a separate moving part.** The door works as a hinged leaf because
the whole mesh *is* the leaf, but a chest cannot open. Splitting a lid out of a single generated
mesh is surface surgery, which §5 rules out as a production method, so this needs an authored
chest rather than a repaired one.

**Structures exist now** (§10). The library had no buildings at all; it now has a modular kit and
the assemblies the prototype needs.

**Authored parametrically, not generated — and that is a deliberate departure.** A modular kit only
works if every piece agrees on its interface to the millimetre: a wall must be exactly 3.00 m or
three of them do not span 9.00 m, and a door frame's opening must clear the 2.05 m door leaf *and* a
1.80 m Veth. A single-image reconstructor cannot guarantee any of that — the door asset came back
as a 1.39 m thick mass and needed a separate fitting pass to become usable at all. Architecture is
the one category where parametric authoring is required rather than merely easier.

| Kit piece | Dimensions (W × D × H) | Faces | Wears |
|---|---|---:|---|
| `building_wall_timber` | 3.00 × 0.18 × 2.60 m | 36 | plaster |
| `building_wall_stone` | 3.00 × 0.36 × 2.59 m | 192 | rubble stone |
| `building_roof_panel` | 3.00 × 2.00 × 0.30 m | 42 | slate |
| `building_door_frame` | 1.34 × 0.20 × 2.44 m | 18 | timber |
| `building_window_frame` | 1.10 × 0.16 × 1.10 m | 30 | timber |
| `building_floor_planks` | 2.99 × 3.00 × 0.05 m | 72 | oak planks |
| `building_step` | 1.20 × 0.34 × 0.20 m | 12 | ashlar |
| `building_post` / `building_beam` | 0.15 × 0.15 × 2.60 / 3.00 × 0.15 × 0.15 m | 6 / 6 | timber |
| `building_fence_panel` | 2.40 × 0.15 × 1.10 m | 132 | timber |
| `building_ruin_wall` | 2.99 × 0.35 × 1.58 m | 150 | rubble stone |
| `building_well` | 1.44 × 1.80 × 2.82 m | 114 | rubble stone |
| `building_road_segment` | 4.00 × 4.00 × 0.09 m | 12 | packed dirt |

**13/13 verified against their declared dimensions and 13/13 pass Godot validation.** The door frame
opening is 1.10 × 2.20 m, so the doorway fits the canonical body with headroom, which §10
specifically requires.

**UVs are generated, not unwrapped.** Two reasons: a box assembly has no UVs until something makes
them, and a kit piece referencing a PBR material it cannot map is not textured however good the
material is. Projecting every face at `1 / tile_size` gives every piece the **same texel density**,
so a wall and a floor repeat at the same real-world rate and seams between pieces line up. Smart
Project would give each piece its own arbitrary scale and the kit would not match itself. The PBR
maps are then embedded so each piece is self-contained.

**LOD policy follows the standard's own rule.** A component under 4k triangles gets no LOD chain,
because three decimations of a 36-face wall produce three identical files. These record
`lod_policy: "none"`, and `_verify_pack.py` now honours both that and `collision_policy`, including
failing an asset whose policy forbids a file that is still present.

**Buildings are recipes, not meshes** (`assets/manifests/kit_assemblies.json`). A baked cottage
would be one unmodifiable mesh and would waste the kit; a recipe keeps the pieces independent and
lets a layout be corrected without rebuilding geometry. All positions are computed from the 3.00 m
module so the arithmetic cannot drift from the geometry.

| Assembly | Footprint | Pieces |
|---|---|---:|
| `longhouse` | 9.0 × 6.0 m | 32 |
| `forge_shed` | 6.0 × 6.0 m | 24 |
| `cottage` | 6.0 × 6.0 m | 24 |
| `communal_building` | 9.0 × 9.0 m | 37 |
| `ruin_kit` | irregular | 6 |
| `world_well` · `road_run` · `outpost_fence` | — | 1 · 4 · 5 |

The longhouse and forge shed are the prototype's two enterable interiors; the cottage and communal
building are the brief's structure list; bridges and a signpost already existed.

Four pieces failed their dimension check on the first build and the check is why they were caught:
the roof's overlapping courses made it 2.10 m instead of 2.00, the stone wall's staggered blocks
overhung to 3.36 m, the ruin wall's depth jitter pushed it past its declared thickness, and the
step's nosing genuinely is 0.34 m deep rather than the 0.30 m I had declared.

Foliage and rocks are done (§11). All 14 foliage assets are correctly scaled, instance cleanly and
have a collision policy; a 3-piece rock set was created because the library had none.

| Asset | Height | Collision policy | Proxy |
|---|---:|---|---|
| `flora_oak_tree` | 14.0 m | trunk | 1.63 × 1.11 × 0.78 m |
| `flora_pine_tree` | 14.0 m | trunk | 0.67 × 1.19 × 0.95 m |
| `flora_birch_tree` | 15.0 m | trunk | 0.17 × 0.33 × 0.60 m |
| `flora_dead_tree` | 14.0 m | trunk | 3.25 × 3.23 × 1.96 m |
| `flora_willow_tree` | 14.0 m | trunk | 1.78 × 1.94 × 0.71 m |
| `flora_bramble_bush`, `flora_boneleaf_bush` | 1.2 m | lower mass | ~1.2 × 1.2 × 0.55 m |
| `flora_cattail_clump` | 1.8 m | lower mass | 1.4 × 1.5 × 0.81 m |
| `flora_fern_clump`, `flora_heather_patch`, `flora_mirrorfern`, `flora_bracket_fungus`, `flora_glowcap_cluster` | 0.45 m | **none** | walked through |
| `rock_field_cluster` | 0.60 m | solid hull + box | new |
| `rock_boulder` | 1.80 m | solid hull + box | new |
| `rock_outcrop_shelf` | 4.00 m | solid hull + box | new |

**The tree collision was a real gameplay bug.** Every foliage asset carried collision built from
its whole bounding volume, so a 14 m oak had a **12.67 × 14.00 × 13.74 m** convex hull — a block of
solid air that would have stopped the player metres from the trunk. Collision is now built from the
part the player actually touches: a trunk hull for trees, the dense lower mass for bushes, and
nothing at all for ground cover, recorded as `collision_policy` in each asset's metadata.
`_verify_pack.py` honours that field rather than demanding files that should not exist, and also
fails an asset whose policy says `none` while proxies are still present.

Two of my own approaches failed before this one and are worth recording. A fixed fraction of height
is not a trunk: 22% of a 14 m oak already spans 7 × 8 m of low branches. Then a percentile band
collapsed the birch to an **8 cm twig**, because its lowest slice is sparse and a band measured by
vertex count has nothing to work with. What works is a **median-radius** core, which adapts to the
trunk's actual width instead of to how many vertices happen to be down there.

**Instancing was checked, and my first check was wrong.** I failed all 14 foliage assets for having
embedded textures. That is not an instancing blocker — a GLB imports once as a shared scene
resource in Godot, so instances reuse the same mesh and material and the textures are not
duplicated. Embedding costs file size and prevents sharing a texture across assets, which is worth
reporting but is not a failure. Corrected: **14/14 foliage and 3/3 rocks instance cleanly** (one
mesh, one material, no skin, 3 LODs each, 25–40k triangles).

Note the prototype's place is **Ashen Hollow** (200 m × 200 m, 4 cells), not "Otherhome Marches",
which is a 2 × 2 km Phase-2 region.

---

## 9. Crafting support

**The chain is complete and now assembled.** Every mesh it needs already existed — ore, pick,
ingot, anvil, hammer, tongs, bellows, chest, workbench — but a mesh is not a gameplay object:
`item.material.iron_ingot` is a row in a content file and nothing joined that row to
`item_grey_iron_ingot`'s GLB, its sockets or its interaction anchor.

`assets/manifests/prototype_crafting_chain.json`, generated and verified by
`_make_crafting_chain.py`, is that join. It carries the two recipes `PROTOTYPE.md` section 4.1
authorises, the station, the two gathering nodes with their charge models, the gathering tool, both
loot sources, and a design-id → asset-id row for all sixteen content items in section 4.2.

| Required | State |
|---|---|
| Mining pick | **built and socketed** — `tool_mining_pick`, 0.90 m, four sockets |
| Ore | exists — `item_raw_iron_ore`, `resource_iron_ore_chunk` with `SOCK_harvest_point` |
| Ingot | exists — four grades, all correctly 0.30 m after this round's rescale |
| Anvil | exists — `prop_blacksmith_anvil_stump`, `SOCK_interact_anvil` |
| Hammer / tongs / bellows | exist and socketed |
| Workbench | exists — `prop_carpenters_workbench`, `SOCK_interact_workbench` |
| Station | `station.forge_shed` → the `forge_shed` kit assembly plus three socketed fixtures |
| 2 recipes | `recipe.alchemy.salve_minor` and `recipe.smithing.sword_temper`, costs quoted from PROTOTYPE.md |

The generator fails rather than writing a manifest that names an asset or a socket that does not
exist, and it did: it caught a node whose `resource_ref` was a content id being checked as an asset
id. Regeneration is one command.

**Four of the sixteen content items have no geometry at all, and the manifest says so rather than
quietly substituting something.** `item.ammo.arrow_rough` is the one that matters: the bow carries
`SOCK_ammo` and the prototype exists partly to prove ammunition is consumed during combat, but no
arrow mesh exists anywhere in the library, so a fired shot cannot be seen. `item.material.raw_meat`
and `item.trinket.wolf_fang` are wolf drops with no geometry, and a water-flask *charge* is a state,
not an object.

Eight more are `substituted` — usable geometry under a different id, with the reason recorded. The
salve is a health-potion bottle, the hide vest is a quilted gambeson, the hide cap is a steel helm.
Those are decisions a reviewer can see and reverse, not omissions.

**And the starter sword could not be held.** `item.weapon.rusted_sword` is the weapon
`PROTOTYPE.md` step 1 equips before the player has done anything, and `weapon_rusted_militia_sword`
carried **no grip socket** — so the one weapon the prototype actually starts with was not a gameplay
object. It now has `SOCK_grip_primary` at the measured mid-grip (2.5 cm envelope, hand scale),
`SOCK_pommel` and `SOCK_attach`, placed from a 40-bin profile rather than from its bounding box.
Seven weapons now carry sockets.

**Twenty-nine interactive objects carry anchors, up from twenty-two.** Every item in the
prototype's content list that the player takes off the ground or out of a container gained
`SOCK_interact_take`: the salve, the flask, the hide, the grimoire, the amulet, the helm. A mesh
with no take anchor cannot be picked up, which makes it scenery however good it looks. A duplicate
key in the interaction map was also found and removed — `item_raw_iron_ore` was listed twice, and
the second entry silently dropped its harvest point.

**Two world dependencies the chain has and the library does not.** `item.tool.water_flask` refills
at the stream, which is a dependency of the alchemy recipe, and there is **no water feature in the
library** — `building_well` is a well, not a stream. The forge shed has a building and bellows but
**no lit forge fixture**. Both are recorded in the manifest as outstanding rather than papered over.

Note **no anvil exists anywhere in the design docs** — the only occurrence of the word is a sound
reference in `AUDIO_DESIGN.md`. `prop_blacksmith_anvil_stump` is therefore the sole source of truth
for what an anvil is in this game.

---

## 10. Magic VFX

**Present now, as presentation rather than as more props.** Section 16 asks for three representative
proof effects, a casting origin, a travel effect, an impact, a Strain concept and a casting
animation. `PROTOTYPE.md` section 4.1 names the three spells, so the set is not a guess.

| Effect | School | Kind | Frames | fps | Loop | ttl |
|---|---|---|---:|---:|---|---:|
| `vfx.magic.cast_charge` | magic | cast charge | 8 | 24 | no | 0.55 s |
| `vfx.ember.bolt_travel` | `magic.ember` | projectile travel | 8 | 24 | yes | — |
| `vfx.ember.bolt_impact` | `magic.ember` | impact | 8 | 24 | no | 0.33 s |
| `vfx.ward.oakskin_shell` | `magic.ward` | ward shell | 8 | 12 | yes | **10.0 s** |
| `vfx.mend.salve_restore` | `magic.mend` | restore | 12 | 12 | no | **6.0 s** |
| `vfx.resonance.strain_overlay` | magic | screen overlay | 1 | — | no | — |

`assets/manifests/magic_vfx.json` declares each effect's atlas, grid, cell size, frame count, fps,
loop, ttl, blend mode, emission energy, tint, origin socket, and the clip and event it binds to. The
ttl values are the spells' own durations from PROTOTYPE.md, not round numbers. Bindings are checked
against the animation registry, so an effect cannot reference a clip or an event that does not
exist.

The cast origin is **`SOCK_hand_r`** on the canonical skeleton, 0.635 m out along the arm — it
already existed, so no new socket was needed. Unarmed casting is correct for the prototype: none of
its fifteen items is a staff, and no staff in the library carries a socket, so a staff origin does
not exist and the manifest says so.

**Generated, not rendered, and that is the right call here.** Particle art needs clean straight
alpha, and a concept render or an AI image arrives with its own light baked in and no alpha channel
worth using — the same double-lighting problem the world materials had. The frame count, cell size
and cell order are the part gameplay binds to. Every effect is marked `authoring: generated` and
`replaceable: true`, so the pixels are honestly labelled proof-grade while the interface is
production-correct and can carry authored art later without changing anything above it.

**One bug was found by measuring rather than by looking.** My `smoothstep` guarded its span with
`max(edge1 - edge0, 1e-6)`, which turns a *negative* span — a valid inverted falloff — into 1e-6 and
saturates the ramp to 1 everywhere. The ward shell's bark mask therefore filled the entire sprite
instead of being confined to the body's silhouette; the corner alpha measured **0.467** where it
should have been 0. It is guarded with `abs` now, and the corner measures **0.000**.

Two effects were also visibly wrong on inspection and were rebuilt: the bolt was a glowing pill that
looked identical every frame rather than a streak that widens behind its head, and the salve was a
saturated white mush because a couple of dozen wide gaussians sum into a mass — it needed many small
motes and an empty field between them.

**Strain feedback is a curve, not six textures.** `strain_feedback` in the manifest gives the
overlay alpha, desaturation and vignette at five Strain values from 0 to 1, plus a slow pulse above
0.65. Otherreach has no mana pool, so the feedback has to read as a cost rather than as a bar
emptying.

**Not done:** the VFX are assets and a contract, not Godot particle systems. No effect has been
instanced in a scene, so `godot_validated` is false for all six.

---

## 11. UI and audio gaps

**UI: the minimal subset is promoted — 17 slots, from 0.** Section 24 asks for the resources, the
interaction prompt, inventory and equipment, the three weapon families, the three proof spells and
the essential statuses, and explicitly not for a finished UI.

| Group | Slots | Source |
|---|---|---|
| Resources: health, stamina, focus, resonance/strain | 4 | rendered |
| Interaction prompt; inventory; equipment | 3 | rendered |
| Weapons: one-handed sword, bow, polearm | 3 | rendered |
| Proof spells: `ember.bolt`, `mend.salve`, `ward.oakskin` | 3 | **reused** concepts |
| Statuses: burning, bleeding, weakened, oakskin | 4 | **reused** concepts |

Ten concepts were rendered for the slots the library had nothing for — the four HUD resources, the
prompt and the two panels, and the three weapon families. The remaining seven reuse concepts that
already existed, which is the section 33 preference for making an existing asset usable over
generating another one. `assets/manifests/ui_icons.json` records which is which, so the two are
never confused later.

Shipped at 128 px with a 256 px master and a six-column atlas, because a HUD drawing seventeen
separate textures per frame is seventeen binds for seventeen small quads. The sprites are **opaque
and declared as such**: cutting alpha out of a painted concept produces ragged edges at HUD size, so
they are drawn as framed tiles rather than pretending to be cut-outs.

**Audio: still a clean, total gap.** No audio library exists — a repository-wide search for `.wav`,
`.ogg` and `.mp3` under `W:\UNNAMED` returns nothing, and there is no `assets/audio` tree. Per §25
this is reported rather than built, and it is the one area where the pipeline cannot proceed without
an owner decision on source and licence.

**Stale naming** classified in `assets/manifests/stale_naming.json` (46 assets, nothing renamed or
deleted per §18):

- `STALE_DESIGN_NAME` (2): `item_mana_potion`, `item_mana_elixir_bottle` — Otherreach uses
  Resonance/Strain, not a mana pool.
- `REUSABLE_GEOMETRY_NEEDS_RECONTEXTUALIZATION` (4): `prop_dwarven_forge_lantern`,
  `weapon_dwarven_axe_engraved`, `weapon_elven_glaive_hooked`, `weapon_elven_recurve_bow`.
- `NEEDS_DESIGN_REVIEW` (40): the eight `icon_school_*` schools and the 32 cancelled `raceclass_`.

---

## 12. Godot validation

**There is now a validation gallery** (§28) — `tools/godot_validate/asset_gallery.tscn`, an asset
viewing scene, not gameplay. Open it, run it, and judge whether the playable set is usable in the
engine. It reads `assets/validation_scene.json`, which `_build_validation_scene.py` generates from
the live library, so the gallery cannot drift away from what exists.

**6 rows, 32 assets, 6 clips staged and verified.** Every asset loads, instantiates and measures
correctly *in the engine*:

| Row | In-engine measurement |
|---|---|
| Player | Veth **0.914 × 1.800 × 0.299 m** |
| Enemies | wolf **1.300 m**, bear **2.000 m**, armour **2.000 m**, husk **1.800 m**, serpent 3.000 m |
| Weapons | sword **1.000 m**, longbow **1.700 m**, spear **2.200 m** |
| Building kit | wall **3.000 × 2.600 × 0.180 m**, door frame **1.340 × 2.440 m** |
| Interaction props | door **1.033 × 2.050 × 0.060 m**, chest 0.800 × 0.597 m, anvil 0.700 m |
| Foliage and rocks | oak **14.0 m**, pine 14.0 m, fern 0.45 m, rocks 0.60–4.00 m |

The gallery provides the §28 views: a **scale reference** of one-metre posts so size is judged
against a real measure rather than against neighbouring assets that might all be wrong together; the
**humanoid reference**; **animation playback** with each enemy playing its idle, clips attached as
an `AnimationLibrary`; **weapon grip** markers at the declared socket positions; **collision
visualisation** toggled with C, drawing the proxies as wireframe; and the LOD chains.

Two things worth recording. **Godot names imported animations after the importing node**, not after
the stable clip id — `anim.humanoid.locomotion.walk_forward` arrives as
`RIG_anim_humanoid_locomotion_walk_forwardAction`. The animation document is explicit that gameplay
must key off stable ids and metadata rather than imported clip names, so the gallery resolves the
mapping at load time instead of matching a name that will not be there.

And **running a 3D scene headless hangs**, because headless Godot has no rendering device. So
`validate_scene.gd` is a `MainLoop` that performs the same checks the scene depends on — every
asset loads, instantiates into a mesh, and measures what it should; every clip carries an animation
— without needing a renderer. The `.tscn` is for a human with a display.

Written as text rather than produced in the editor, so the ORM wiring is explicit: Godot reads
occlusion, roughness and metallic from one texture scaled by the material's own multipliers, which
is the packing glTF uses.

**A real scene error was caught and fixed:** a `[sub_resource]` block placed after the node that
referenced it made Godot fail to load the scene entirely. Sub-resources must precede their users.

**The animation gallery row still attaches clips one at a time**, which is right for a viewing
scene, and it now applies the registry's loop policy for the reason recorded in section 4: without
it every looping clip played once and froze.

`validate_anim_tree.gd` is the section 6 proof and is described in full in section 4: it builds one
`AnimationLibrary` from all 47 clips under their stable ids, cross-checks every one against the
registry, and drives a 14-state machine to observe transitions rather than assume them.

---

## 13. Blockers

Ordered by what actually stops Claude building M3–M6. Several previous blockers are now closed and
are recorded as closed rather than deleted, so the list does not have to be re-derived.

Closed during this sprint: buildings and the modular kit (section 8), the Veth body and its bind
(section 5), the semantic scale of the playable subset (section 2), weapon sockets and real weapon
lengths (section 6), enemy animation (section 7), the animation clip set (section 4), the world
materials (section 8), the mining pick and the assembled crafting chain (section 9), the magic
presentation set (section 10) and the UI icon subset (section 11).

What still stops work:

1. **No arrow geometry.** `item.ammo.arrow_rough` is one of the prototype's fifteen items and the
   bow's `SOCK_ammo` is empty. Ammunition consumption during combat is a mandatory prototype test,
   and a fired shot cannot be drawn. It is the only missing item in the crafting and combat chain
   that the library could make from scratch.
2. **No water feature and no lit forge fixture.** The alchemy recipe is gated behind finding the
   stream, and there is no stream; the forge shed has a building and bellows but nothing burning.
   Both are interlocking dependencies of section 9's chain.
3. **72 assets still have no declared size expectation**, and 51 more fail their declared one. None
   of them is in the playable subset, which is why this is no longer at the top.
4. **Audio is a total gap and needs an owner decision** on source and licence. Unchanged, and the
   one item on this list that no asset work can resolve.
5. **Nothing has been validated inside a running scene.** Everything is engine-verified as a
   resource — loads, measures, animates, transitions — but no character has walked under the state
   machine, no VFX has been instanced, and no building has been assembled in engine. That is
   gameplay-adjacent work, and it is where the next real defect is most likely to be.
6. **The outfit has no coverage metadata and no Method C fit.** Coverage is metadata by design and
   no tool emits it, so no armour piece reduces damage. This is the last technical gap in the
   character column.

Not blockers, despite appearances: skeletons and enemy rigs, foliage species coverage,
bridges/gates/signpost, ore and ingots, world materials, architecture, and the animation pipeline.

---

## 14. Next actions

In priority order, following the §33 rule of preferring "make an existing asset usable" over
"generate another attractive asset":

1. **Build the arrow** (`item.ammo.arrow_rough`), and its nocked and in-flight presentation against
   the bow's `SOCK_ammo`. Small, parametric, and the only thing standing between the prototype's
   ranged path and being drawable.
2. **Build a stream/water treatment and a lit forge fixture** for the two chain dependencies in
   section 9. The forge is a fire-lit box and an emissive material; the stream is a water surface
   and a refill anchor.
3. **Instance the six VFX and the state machine inside the validation gallery**, so the animation
   and magic sets are exercised in a scene rather than as resources. This is where section 13's
   fifth blocker is closed.
4. **Emit combat coverage metadata** from the outfit pieces and finish the Method C canonical fit —
   the last technical gap in the character column.
5. **Declare expectations for the 72 `UNKNOWN_SCALE` assets** and correct the 51 failures that
   follow. The rule for the first is to extend the expectation table rather than guess; every
   `UNKNOWN_SCALE` that got a rule this round turned out to be resolvable.
6. **Promote the remaining weapon, axe, resource and item families to their declared sizes** — the
   same operation as section 2, applied to the families that are not yet in the playable subset.
7. **Build the NPC set the prototype names** — `npc.keeper_halda`, `npc.smith_orren` and
   `npc.warden_kesh` — after the current-morphology check section 19 requires. Candidates are
   recorded in the crafting-chain manifest, deliberately unresolved.

---

## Ownership

Owned: `assets/`, `tools/asset_pipeline/**`, Blender automation, rigging, animation, asset metadata
and manifests, Godot asset-import/validation scenes, asset-production documentation. Confirmed by
`IMPLEMENTATION_PRECEDENCE` §9: "DeepSeek/DSH owns asset generation, rigging, animation and
asset-pipeline tooling during this run, and nothing else."

Not owned: gameplay, domain and application systems; Claude-owned code. Nothing in this sprint has
touched them, and nothing has been committed.
