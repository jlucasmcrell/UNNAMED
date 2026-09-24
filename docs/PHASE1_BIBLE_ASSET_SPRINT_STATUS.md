# Phase-1 bible asset sprint — status

Opened after the audio sprint, on the instruction to work through
`PHASE1_ASHEN_HOLLOW_PLAYABLE_CONTENT_BIBLE.md` and get as much **image and 3D** work done as
possible. Per the bible's section 38, this pipeline owns player assets, animation, weapons, enemies,
environment kit, materials, VFX and asset validation; gameplay belongs to Claude.

The method is the one that has worked all along: read what exists, measure it against what the bible
asks for, fix what is wrong before adding what is missing, and look at every output rather than
trusting an exit code.

---

## 1. The headline finding

**Most of the content the bible names already exists. What was wrong was its size, its animation,
and its names.**

The five creature archetypes (Ash Ember Hound, Bone Walker Husk, Animated Armour, Bristleback Boar,
Cave Hunting Spider) were all present, rigged, with concepts and LODs. What they were *not* was:

- correctly scaled — the hound was 1.8 m, 38% larger than it should be;
- animated — three of the five had **zero** animation clips, because the animation builder's roster
  was the superseded one (`frost_wolf`, `highland_brown_bear`, `great_river_serpent`);
- consistent — the rigged copy the game renders was still at the old size after the unrigged one was
  corrected.

The same pattern held for magic: the VFX, the UI icons and the spell ids were all built for
`PROTOTYPE.md`'s `ember`/`oakskin`/`salve` spells, which the bible replaces with **Force, Warding
and Vital**. The art was showing a fire bolt for a kinetic shove.

## 2. Scale corrections

`_rescale_glb.py` corrects assets the pipeline normalised by longest axis to a per-category default.
Seven bible-critical assets were outside the pass band:

| Asset | Was | Now | Why it matters |
|---|---|---|---|
| `creature_ash_ember_hound` | 1.80 m | **1.30 m** | A hound was the size of a person. |
| `creature_cave_hunting_spider` | 1.80 m | **1.20 m** | Measured across the leg span. |
| `creature_bristleback_boar` | 1.80 m | **1.50 m** | Stocky quadruped. |
| `weapon_recurve_hunting_bow` | 1.20 m | **1.70 m** | A bow shorter than the archer. |
| `weapon_hunting_belt_knife` | 1.20 m | **0.60 m** | A belt knife was sword-length. |
| `resource_iron_ore` | 0.50 m | **0.20 m** | A hand-sized ore chunk was half a metre. |
| `resource_iron_ingot` | 0.50 m | **0.30 m** | Ditto. |

Catalog rebuilt and re-audited afterwards, because the audit reads expected sizes out of the catalog
and a rescale alone leaves it stale. `PASS_SCALE` went **233 → 239**.

### The rigged copies were a trap

`_rescale_glb.py` refuses skinned meshes, and it is right to: a skinned GLB carries inverse bind
matrices expressed in the old scale, so moving vertices alone tears the character apart. That left
the three creatures' `rigged/` GLBs — **the copies the game actually renders** — still at 1.8 m while
their `ready/` meshes were corrected. A 38%-oversized asset that looks fine in the audit is worse
than one that fails it.

A Blender round-trip was tried first and rejected: it exported with the accessor bounds at 0.836 m
and a compensating node transform, which is not what the pipeline wants.

The clean fix was to **re-rig from the corrected mesh**, which the rig tool supports directly. All
three now match their `ready/` meshes exactly, and the operation also proved the rig is reproducible:

```
creature_ash_ember_hound_rigged.glb      dims 0.973 x 1.300 x 0.827 m  longest 1.300  skins=1
creature_bristleback_boar_rigged.glb     dims 1.460 x 1.250 x 1.500 m  longest 1.500  skins=1
creature_cave_hunting_spider_rigged.glb  dims 1.200 x 0.863 x 1.061 m  longest 1.200  skins=1
```

18 bones and **0 unweighted vertices** on each. The stale copies are archived in
`assets/_superseded/rigged_prescale/`, not deleted. Both extremes were then rendered and looked at:
the hound reads as a lean long-legged canine, the spider as a proper eight-legged arthropod, and
neither shows any mesh tearing.

## 3. The five archetypes now have animation

`_build_creature_anims.py` built six clips per enemy — idle, walk, run, attack, hit, death — but its
`ENEMIES` roster was still the superseded five. Two of the bible's archetypes (the husk and the
armour) were covered; **the hound, boar and spider had no clips at all.**

The roster is now the bible's five, with the superseded three kept below it so their existing clips
stay reproducible, plus a `PHASE1_ROSTER` list so a plain `--apply` cannot silently re-render
finished work.

```
30 clips built, 0 failed
```

The 18 new clips are on disk and their metadata is registered. All 18 were then **rebuilt against
the newly re-rigged creatures** and passed with **0 bones missing**, which is the check that proves
the clips still bind after the rig changed. The animation GLBs are animation-only
(`meshes=0, skins=1, animations=1`), so they bind by bone name and the re-rig could not invalidate
them — worth verifying rather than assuming.

**Known limitation, recorded:** the spider is rigged to the `quadruped` plan because that is the only
legged plan the rig tool has. Its eight legs therefore cannot articulate independently and its walk
reads as a four-limbed gait. That is a limitation of the rig family, not of the motion, and it needs
an arthropod rig plan to fix.

## 4. Magic retargeted to the bible's three formulas

The bible's section 13 names **Impulse Bolt (Force)**, **Brace Ward (Warding)** and **Mending Thread
(Vital)**. The VFX were built for `spell.ember.bolt`, `spell.ward.oakskin` and `spell.mend.salve`,
which is not merely a naming difference — it put a **fire** bolt on a kinetic spell, **bark bands**
on a resonant ward, and **green motes** on a thread.

Effect ids, schools, palettes and shapes were all retargeted. The interface the game binds to (cell
size, grid, frame count, fps, loop flag, ttl, origin socket, bound clip event) is unchanged, so no
downstream work moves.

| Effect | Was | Now |
|---|---|---|
| `vfx.force.impulse_bolt_travel` | orange fire streak | cold pale-blue displaced-air slug with a trailing collar |
| `vfx.force.impulse_bolt_impact` | nine flame spikes | an expanding pressure ring that thins as it travels |
| `vfx.warding.brace_ward_shell` | bark bands | a tensioned oval boundary with concentric rings and radial seams |
| `vfx.vital.mending_thread_restore` | 96 green motes | 88 fine vertical filaments that knit |

### A real bug this surfaced

The Strain screen overlay was **generated at 256 px and pasted into a 128 px atlas cell**, so the
manifest shipped the vignette's top-left *corner* instead of the vignette. It was invisible in the
numbers and obvious in the render. Cell size is now declared per effect, with a hard check that
raises if a generator produces a cell that does not match its declaration.

Verified by measuring the shipped atlas rather than squinting at it: centre alpha **0.0** (the world
stays readable at high Strain, which is the whole design intent), edges 99–108, corners 211.

The impact effect needed a second pass after looking at it: the first retune opened on a saturated
blob the size of the cell, so the expansion had nowhere to go. It now opens on a small tight centre.

## 5. Buildings should be assembled, not reconstructed — and the assemblies were never instantiated

This is the most consequential finding of the night, and it arrived by following a failure.

The smithy and the lodge were specified as single-mesh assets and put through the image-to-3D
pipeline. The smithy's **concept is excellent** — a readable open-fronted timber workshop with a
shingled roof, stone forge hearth, workbench and heavy posts. The **built mesh is a jumbled mass of
intersecting planes** that reads as nothing at all.

So the fault is not the concept. It is the reconstructor: buildings have large open interiors, thin
structural members and interior surfaces visible through the opening, which is a hard class for a
single-image reconstruction.

The pipeline already has the right answer. `kit_assemblies.json` declares the prototype's buildings
as **data** laid out from the modular kit:

| Assembly | Declared | Is |
|---|---|---|
| `forge_shed` | 6 × 6 m, 2.6 m walls, 24 pieces | Kera's smithy; "holds station.forge_shed, the anvil and the bellows" |
| `longhouse` | 9 × 6 m, 2.6 m walls, 32 pieces | the communal hall, with a south door |

`_blender_build_kit.py` states why this is not merely easier but **required**: a modular kit only
works if every piece agrees on its interface to the millimetre — a wall must be exactly 3.0 m so
three span 9.0 m, and a door frame exactly 2.20 m so a 1.80 m Veth fits under it. A reconstructor
cannot hold that. The kit pieces themselves are all built and present; the assemblies reference only
`building_wall_stone`/`_timber`, `building_floor_planks`, `building_post`, `building_beam`,
`building_roof_panel`, `building_door_frame` and `building_window_frame`, and **all seven exist**.

**But nothing in the pipeline ever turned an assembly into a mesh.** The buildings have been declared
since the kit was authored and have never existed as assets.

So a new instantiator was written (`_blender_instantiate_assembly.py`). It found three real bugs,
each by probing the engine rather than reasoning about it:

1. Objects arrive from Blender's glTF importer in **QUATERNION rotation mode**, so writing
   `rotation_euler` did nothing at all — every rotation was silently dropped, including the roof
   pitch, and two roofs lay flat.
2. The importer **bakes the Y-up conversion into the geometry**, so re-applying it double-converts.
3. An assembly `[x, y, z]` position has `y` as height and maps to a Blender location `(x, -z, y)`.

**It works now.** Four bugs had to be fixed, and the fourth was not in the instantiator at all — it
was in the generator. `_make_kit_assemblies.py` placed the roof panel centres at
`half_d + ROOF_SLOPE/2 - 0.30`, which is **3.7 m** from the centre of a 6 m building. A 2 m panel
pitched at 32° spans only **1.70 m** horizontally, so the entire roof sat *outside* the walls —
covering z 2.85–4.55 against a wall line at 2.91 — floating clear with the ridge wide open, and
making a 6 m building measure **9.4 m** of depth. The roof position is now computed along the actual
roof plane, from eave to ridge, with as many panels per slope as the half-depth needs.

Both buildings now assemble correctly and are in the library:

| Assembly | Declared | Assembled | Pieces | Triangles |
|---|---|---|---|---|
| `forge_shed` | 6 × 6 m | 6.0 × 6.3 m, 4.97 m to the ridge | 28 | 2 772 |
| `longhouse` | 9 × 6 m | 9.0 × 6.3 m, 4.97 m to the ridge | 38 | 2 628 |

They are in `assets/ready/` with metadata, backfilled, and in `catalog.json`. Rendered and looked at:
with the material stripped the forge shed reads cleanly as four walls, a gable and a door gap, and
the longhouse as a 9 × 6 m hall with corner posts and a door.

**Two false alarms cost time here and are recorded so they are not re-chased.** The wall piece renders
as a field of triangular shards, and stripping every texture left the shards in place — which normally
proves bad geometry. It is not: `building_wall_stone` is deliberately about thirty small jittered
stone blocks, so it renders busy by design, and `_check_glb_health.py` found 0 corrupt GLBs across
2872 files. The "comb" look of the assembled walls is that same rubble texture under the preview's
flat lighting, not gaps.

**What remains for buildings is appearance, not structure.** The kit pieces render near-white and
washed out, so a timber hall currently looks like a paper model. `_blender_build_kit.py` names
materials (`material_rubble_stone_wall`, `material_limestone_ashlar`); whether those are actually
bound to the exported pieces is the next thing to check, and `_verify_pbr_materials.py` already
exists for it.

**Nothing architectural should go through the image-to-3D reconstructor either** — the smithy concept
is a good drawing and its reconstructed mesh is a jumbled mass. `building_smithy` and
`building_lodge` are therefore marked `"build": false` in the batch request and kept as **art
direction**, with their assembly named (`forge_shed`, `longhouse`); their failed reconstructions are
quarantined in `assets/_superseded/reconstruction_failed/`. Between the two routes, buildings have no
usable path yet, which makes them the most important missing assets in Ashen Hollow.

## 6. Missing content: the four-cell environment kit

This is the largest genuine gap, and it is what makes the difference between a test map and a place
the bible describes. Landmarks that are quest-critical or identity-defining were simply absent:

| Missing | Bible | Why it matters |
|---|---|---|
| `landmark_ashen_waystone` | §5, §18 | The spawn point and respawn landmark, and the tallest fixed object in the settlement. |
| `building_smithy` | §5 | Kera's smithy; its chimney smoke is meant to be visible from the spawn. |
| `building_lodge` | §5, §26 | The communal steward building, and a camera-test interior. |
| `prop_iron_vein_outcrop` | §7, §15 | Quest 1's objective. The player must see at a glance that it is ore. |
| `prop_blocked_shaft` | §7 | Communicates that the world continues past the prototype. |
| `prop_quarry_winch`, `prop_quarry_rail_track` | §23, §4 | The quarry should read as worked, not empty. |
| `landmark_quiet_stone`, `landmark_foldscar_core` | §8, §16 | Quest 2 in full: three markers and the central Foldscar. |
| `prop_cart_damaged_merchant` | §6, §29 | Where the bow is found. |
| `resource_ash_haft`, `resource_iron_billet`, `resource_woundmoss` | §6, §12 | The craft chain's inputs and output. |
| `weapon_march_spear`, `weapon_hunting_bow` | §11 | Two of the three frozen weapon families. |

Fifteen assets were specified with physical prompts in the established house style and are being
built. Progress is tracked by `_build_bible_batch.py --audit`.

### The 0.5 m waystone

The first build of the waystone came out **0.5 m tall** — the generic `prop` category default, the
same size as a barrel — because nothing told the pipeline otherwise. The next fourteen would have
done the same.

So sizes are now declared in `assets/manifests/semantic_dimensions.json` as subject rules (15 added,
135 total), which is what the scale audit resolves against, and the same numbers are passed to the
builder as `--target-size`, which is what actually sets the scale. The mis-scaled waystone is
archived rather than overwritten.

`_build_bible_batch.py` is idempotent: it skips an asset whose meta already records the declared
size, archives and rebuilds one that disagrees, and reports ids still waiting on a concept so it can
be re-run as the concept batch lands.

## 7. The skeleton map: nothing in the animation library is orphaned

The NPCs looked like a gap. All four (`renn_vale`, `kera_voss`, `sel_arien`, `tavar_orr`, mapped onto
`npc_veth_magistrate`, `npc_kal_smith`, `npc_siann_archivist`, `npc_orenth_guide`) are rigged at the
correct 1.8 m with `fit_family: standard_humanoid` — but a search of the clip registry found
**zero clips for any of them**.

That looked like the creatures' problem repeating: rigged, unanimated. It is not. Comparing bone
sets rather than clip names gives a different answer:

| Clip skeleton | Bones | Rigged assets that bind to it exactly |
|---|---|---|
| `quadruped_18` | 18 | 50 |
| `creature_humanoid_20` | 20 | 31 |
| `humanoid_standard` | 52 | 2 |
| `other_5` | 5 | 2 |
| **no match** | — | **0** |

**Every one of the 85 rigged assets binds exactly to some clip skeleton.** Nothing is orphaned.

The NPC skeleton is *bone-for-bone identical* to the humanoid creature clips — `bone_walker_husk` and
`animated_armour`, twelve clips covering idle, walk, run, attack, hit and death. So the four NPCs are
**animation-capable today** by reusing clips that already exist, which is exactly the "retarget
existing mismatched assets where sound" the objective asks for. It also means the humanoid enemies
and the NPCs deliberately share one skeleton and therefore one clip set, which is the efficient
arrangement rather than an accident.

The same check also corrects an earlier alarm of mine: the 17 player clips on `humanoid_standard`
looked orphaned against the creatures, which have only 3 or 4 bones in common. They are not — two
rigged assets bind to that 52-bone skeleton exactly.

**What genuinely does not exist for the NPCs** is NPC-specific motion: talking, handing something
over, working at the forge or the survey table, sitting. For M6 their idle and walk come free; the
social and station animations do not exist yet.

## 8. The five archetypes, all five now looked at

Section 10 of the bible asks for five *distinct* archetypes, "not one creature under five labels", so
calling them addressed needs each one actually seen. At corrected exposure:

| Archetype | Reads as | Body plan |
|---|---|---|
| Ash Ember Hound | lean long-legged canine, charred hide with orange ember veins along the flanks | 18-bone quadruped |
| Bone Walker Husk | complete human skeleton — ribcage, spine, pelvis, horned skull | 20-bone humanoid |
| Animated Armour | upright humanoid in dark plate — helmet, pauldrons, breastplate | 20-bone humanoid |
| Bristleback Boar | stocky quadruped | 18-bone quadruped |
| Cave Hunting Spider | eight-legged arthropod, rounded abdomen, cephalothorax | 18-bone quadruped (limitation below) |

All five are distinct in silhouette and none is a relabelled copy of another.

## 9. The three weapon families, and a modern bow in a frontier setting

The bible names three weapon families: arming sword, hunting bow, march spear. All three now have a
settled answer, but only after a call I got wrong and then reversed.

| Family | Asset | State |
|---|---|---|
| Arming sword | `weapon_arming_sword` | existing, 1.00 m, LODs present, Godot `RESULT: OK` |
| March spear | `weapon_march_spear` | **built**, 2.20 m, rendered and looked at, Godot `RESULT: OK` |
| Hunting bow | `weapon_hunting_bow` | **built**, 1.70 m, rendered and looked at, Godot `RESULT: OK` |

**All three families are now complete.** The March Spear came out as specified — a slim forge-dark
leaf head on a socket collar, a long pale ash haft, a leather grip band and a plain conical iron
shoe, with no ornament. It is the one asset of the three the player actually crafts (raw iron ore to
Iron Billet to March Spear), so it needed its own id rather than reusing `weapon_boar_spear_hunting`.

The new hunting bow is a single continuous wooden stave with recurved tips, a thin linen string, a
dark leather grip wrap and an arrow — and crucially **no stabilisers, no sight bracket and no cable
guard**, which is what makes it the frontier weapon and the compound bow not one. Reversing that call
was right, and the two renders side by side settle it.

`weapon_recurve_hunting_bow` was booked for regeneration over a scale defect — it audited
SUSPECT_SCALE at 1.20 m against an expected 1.70 m. That defect was fixed this sprint by rescaling it
to 1.70 m, so I briefly marked the bible-named bow as **retargeted to the existing asset** on the
grounds that a correct-sized bow already existed and a second one would split the family.

**Rendering it is what showed that was wrong.** `weapon_recurve_hunting_bow` is a **modern compound
target bow**: a black riser carrying a sight bracket, a long-rod and a side-rod stabiliser crossing at
the middle, and a cable guard. None of that belongs in Ashen Hollow, which is iron, ash and linen. The
size audit could not see this — the asset measures a perfectly correct 1.70 m — and only the render
did. So the request was reverted to a build and both bows keep a declared expectation, with a note on
each explaining why two 1.70 m bows exist.

The general lesson, which is the same one the buildings taught: **a size audit and a manifest check
cannot tell you an asset is the wrong object.** Both of this sprint's most consequential findings —
the compound bow and the smithy that reconstructed into a jumble — were found by looking at a render,
and neither was visible in any number.

## 10. Godot validation now covers this round's output

The previous round's 3D output had been checked by measurement and by eye only. That gap is closed:

| What | Result |
|---|---|
| **Every one of the 13 bible build targets** | **`RESULT: OK`, and all 13 record `godot_validated: true`** |
| `forge_shed`, `longhouse` | `RESULT: OK` |
| `weapon_arming_sword`, `weapon_recurve_hunting_bow` | `RESULT: OK` |
| The five rigged archetypes | `RESULT: OK` |
| **The whole animation library** | **65 passed, 0 failed, 0 missing** |

**The result is now durable, which it was not before.** `godot_validated` rides on every asset as a
provenance field, and `_backfill_metadata.py` was writing a permanent `False` with the comment "never
tested" — so it could never become true and the manifests reported every asset, including passing
ones, as unvalidated. A provenance field that cannot be true is worse than absent, because it
silently asserts the opposite of the truth. `_godot_validate_assets.py` now writes
`godot_validated: true` and the date into the asset's own metadata on success, so running the
validator records what it proved instead of printing it once and losing it.

The forge shed's import also independently confirms the material fix: Godot reports **28 mesh
material slots resolving to 7 distinct materials**, which is exactly the batching the kit needs and
would have been 28 unique materials before the fix.

## 11. A rule is not applied just because it is written: the family field

Two findings, one of which corrects a claim in an earlier section of this very document.

**The audit only applies a subject rule whose `family` field equals the asset id's prefix.**
`resolve_expectation` skips every rule where `rule["family"] != asset_id.split("_")[0]`. The size
rules I wrote earlier used the *semantic* family — `prop`, `building`, `resource` — which happens to
work for `prop_iron_vein_outcrop` and `resource_ash_haft` because their prefixes match, and silently
does nothing for anything starting `landmark_`.

So **the three actual landmarks — the Ashen Waystone, the Quiet Stone and the Foldscar core — resolved
to no expectation at all** and were audited as `UNKNOWN_SCALE`. Section 6 says their sizes were
declared, and they were declared, but the audit could not see the declarations, so the claim that
they were validated was wrong. The rules now carry the id prefix, and all three resolve correctly
(2.80 m, 2.20 m, 6.00 m), as does `npc_kal_smith`. The audit moved accordingly: `PASS_SCALE` 248 to
245, `SUSPECT_SCALE` 124 to 131, `UNKNOWN_SCALE` 77 to 73.

The lesson is worth more than the fix: a rule that is present, well-formed and plausible is not a
rule that is applied. The only way to know is to ask the resolver, which is what
`_probe_expectation.py` now does.

**Every race in the library was normalised to the Veth height — now fixed.** `family_defaults` set
`race`, `racebody`, `race2`, `raceclass` and `npc` all to **1.8 m** — which is the Veth height and only
the Veth height. `CANONICAL_BODY_AND_SKELETON.md` gives four families: `standard_humanoid` 1.80 m
(Veth, Siann, Orenth), `compact_broad` **1.30 m** (Kal, "broad and short"), `tall_narrow` 2.35 m
(Vaskaal), `irregular_heavy` 2.60 m (Ondrek).

Four Kal assets measured exactly 1.800 m. The worst was `npc_kal_smith` — **Kera, the smith who runs
the forge the player crafts the March Spear at** — which would have stood 0.5 m taller than every Veth
in the settlement and been handed standard-humanoid-fit armour.

All three Kal bodies are now correct, by direct measurement of the GLB accessor bounds:

| Asset | Was | Now | Fit family |
|---|---|---|---|
| `npc_kal_smith` | 1.800 m | **1.300 m** | `standard_humanoid` → **`compact_broad`** |
| `race_kal_representative` | 1.800 m | **1.300 m** | `compact_broad` ✓ |
| `racebody_kal_pose` | 1.800 m | **1.300 m** | `compact_broad` ✓ |
| `npc_veth_magistrate` | 1.800 m | 1.800 m ✓ | unchanged — correctly left alone |

The `fit_family` fault had its own root cause. `_backfill_metadata.py`'s `FIT_RULES` does list the Kal
under `compact_broad`, but its tuple named only `race2_kal`, `race_kal`, `racebody_kal` and
`raceclass_kal` — **`npc_kal` was missing** — so the smith fell through to the `npc_` catch-all. One
word, and the same shape of bug as the family-field fault above.

**How the fix was verified, and one thing that was not.** Each asset was rescaled and then
**re-rigged**, because the `rigged/` copies are what the game renders and a skinned mesh carries
inverse bind matrices in the old scale — the same trap that left the three creatures oversized earlier
in this sprint. All three came back with **20 bones, 0 unweighted vertices** and an **exact
bone-for-bone match** against the `creature_humanoid_20` clip skeleton, so the existing clips still
drive them. Godot reports `RESULT: OK` for all three.

A side-by-side render was attempted to show the Kal "broad and short" next to a Veth. It renders and
the proportions read correctly, but **its vertical framing is wrong**: two rigged GLBs arrive with
their geometry at origins 1.0 m apart, so one scene-level grounding shift grounds one character and
lifts the other. The script is marked unreliable in its docstring, its output was deleted from the
review folder rather than left as false evidence, and the claim above rests on measurement — which is
the stronger evidence for a size question anyway.

**One more discrepancy, not yet resolved.** `CANONICAL_BODY_AND_SKELETON.md` calls the canonical
humanoid skeleton **24-bone**, and the four NPCs carry a **20-bone** rig (`npc_orenth_guide`, and the
other three alike). They bind perfectly to the `creature_humanoid_20` clip family, so they work, but
if armour is authored against the canonical skeleton as that document says, the NPCs are not on it.
Whether the document is stale or the NPCs are mis-rigged is unresolved, and it is worth resolving
before armour production, not after.

## 12. Four library-wide bugs found while integrating the new assets

All four were pre-existing. The first three were found because the new assets had to pass through the
same steps as everything else; the fourth was found by measuring a material instead of trusting a
render.

**1. `godot_validated` was a provenance field that could never be true.** `_backfill_metadata.py`
wrote a permanent `False` with the comment "never tested", so every asset in the library — including
the ones that pass — was recorded as unvalidated. See section 10: the validator now writes its own
result, and all 13 bible targets carry `godot_validated: true` with a date.

**2. The metadata backfill had been broken for 189 assets.** `_backfill_metadata.py` adds the
provenance fields every asset is supposed to carry — `status`, `modular_interface_version`,
`lod_status`, `collision_status`, `fit_family`, `license_notes`, the paths. It crashed with
`'str' object has no attribute 'get'`. The cause: it assumes every manifest's `assets` key is a
**list of records**, while `semantic_dimensions.json` uses a **dict keyed by asset id**, so iterating
it produced string keys. It now handles both shapes.

Fixed and re-run: **189 assets updated, 299 already complete, 0 failed.** The new landmarks came out
of it with full provenance (`status: export_ready`, `lod_status: present`, `collision_status:
present`, 25 000 triangles), and so did 186 older assets that had been silently missing it.

**3. Two batch drivers were building the same asset at once.** A stale `_build_bible_batch.py` run was
still working through its list when a fresh one started, so both spawned `_make_assets.py` for
`prop_quarry_winch` and raced on the same asset — one succeeded, the other failed `rc=1` with "0/1
verified" while ASTRAL sat idle between the two. Nothing was corrupted, but the pass was paid for
twice. Worth knowing that two drivers sharing one concepts folder have no mutual exclusion.

**4. The preview renderer was over-exposing every asset review by about six times.** All three
three-point lights were set to `800 * radius^2`, which is roughly six times the irradiance that
lands a mid-albedo surface at middle grey. A weathered timber wall and a limestone wall rendered
both pure white, and the Ash Ember Hound lost its ember cracks entirely — it read as a plain grey
dog. **Every review image in this sprint was made under that exposure**, so any judgement from a
render of a pale, bright or low-contrast asset has to be treated with suspicion.

The lights now carry a per-light key/fill/rim ratio summing to about the correct total. Re-rendering
at the fix **changed nothing about the geometry verdicts** and materially improved the material ones:
the hound now shows its charred hide with orange ember veins running along the flanks, and the forge
shed reads as a timber-and-slate building rather than a paper model. The fix was validated by
re-rendering the creatures before trusting it.

**3. The assemblies had one material per piece.** Importing the same kit piece twenty times gave
Blender `MAT_building_roof_panel`, `.001`, `.002` … — twenty identical materials against three shared
textures. The first longhouse shipped **38 materials for 38 pieces**. Since the entire value of a
modular kit is that an engine can batch and instance repeated pieces, a unique material per piece
defeats the kit: 38 draw calls for one building, and 760 for a street of twenty. The instantiator now
collapses materials by base name, and both buildings ship **7 materials** — one per distinct piece —
with no change in appearance.

**4. `catalog.json`'s dimension field is empty for many assets**, including the new ones. The scale
audit reads its measurements from the metadata and the GLB rather than from the catalog, which is why
the audit works and this is cosmetic — but it means `catalog.json` is not a reliable place to read an
asset's size from. Not fixed; recorded.

## 13. HUD icons

The bible's section 19 names the HUD's contents and section 22 prefers a compass over a minimap. The
existing icon set had **the same superseded spell problem** — a flame burst standing in for Force, a
stone-skin plating for Warding, a generic heal for Vital — and had no icons at all for the hotbar's
restorative and torch, for companion Follow/Wait, for the tracked objective or for the compass. It
also shipped an `oakskin` status for a spell that no longer exists, where the bible names Bleeding,
Wounded, Strained, Burning and Weakened.

**The set is now built.** All 13 concepts rendered and the promotion ran: **26 slots, 23 rendered and
3 reused**, composited into a 6×5 atlas at 128 px, covering the four resources, the interaction
prompt, inventory, equipment, the four weapon slots including the crafted March Spear, the three
Phase-1 formulas, the statuses the bible names, the hotbar's restorative and torch, companion
Follow/Wait, the tracked objective and the compass. The promotion tool **fails loudly and names what
is missing** rather than emitting a manifest that references absent files, which is how the gap was
found in the first place.

Judged at full size rather than as atlas thumbnails — `_crop_icon.py` reads the atlas ordering out of
the manifest and crops a named cell, because a 6×5 atlas reaches the eye at about 125 px per cell,
which is enough to see that an icon is unreadable but not enough to see what it shows.
`ui.status.wounded` is the weakest of the set: it renders as a **bandage** rather than the gash that
was specified. Read against `ui.status.bleeding`, which is blood drops, that is a coherent pairing —
blood for bleeding, a dressing for wounded — so it is legible and kept, but it is the first candidate
for regeneration.

## 14. Checking the objective item by item, and three things that fell out

`_audit_objective.py` walks every deliverable the objective names and reports, per asset, whether a
GLB exists, its measured longest axis, whether collision is present, whether Godot has signed off and
whether there is a render to look at. It cannot know whether a render was *looked at* — that record is
kept here — but it does stop "verified" being a summary instead of a per-item claim.

It went **from 27 open flags to 0**, and the three findings are worth more than the green result.

**`godot_validated` was being erased by the backfill.** Everything showed `ungodot`, including assets
that had passed minutes earlier. The cause: `_backfill_metadata.py` wrote `godot_validated: False`
unconditionally, so the `--force` run after the Kal work **reset the True that
`_godot_validate_assets.py` had recorded**. The earlier fix — making the validator write its own
result — was therefore incomplete, because a second writer was still clearing it afterwards. The field
is now owned solely by the validator and the backfill does not touch it. All 30 assets were
re-validated, and the flag surviving a `--force` backfill is the check that proves it.

**The buildings had no collision.** A single convex hull is not merely a coarse proxy here, it is the
wrong one: Ashen Hollow's two buildings are "enterable interiors", and a hull around the whole
assembly **seals the doorway the building exists to have**. The instantiator now emits one box per
solid piece — 27 for the forge shed, 37 for the longhouse — and **skips `building_door_frame`
entirely**, because that is the piece defining the entrance and boxing it would wall up the door with
the geometry meant to frame it. The kit's own convention is box collision, so this matches it rather
than inventing a second scheme.

The `no-lod` flag on the buildings was a **false positive in my own audit** and is no longer raised:
the kit pieces carry no LODs by convention, so an assembly of them carrying none is consistent, and a
2 700-triangle building is not what LODs are for. Flagging it would have pushed someone into
manufacturing LODs to satisfy a check rather than fixing anything.

**A duplicate iron ore, and I had rescaled the wrong one.** The objective names `raw_iron_ore`. The
crafting chain resolves `item.material.iron_ore` to **`item_raw_iron_ore`** as `"exact"`, and that is
the id in `playable_prototype_assets.json`. The library also holds a `resource_iron_ore`, and earlier
in this sprint I rescaled *that* one from 0.50 m to 0.20 m believing it was the bible's item. Rendered
side by side they are not the same object at all: `item_raw_iron_ore` is a rough rock with metallic
ore inclusions, which is right, and `resource_iron_ore` is a **stylised hexagonal crystal with orange
veins in a white frame** — a fantasy mana crystal, not ore. Nothing consumes it, but the rescale was
aimed at the wrong asset and the wrong asset must not be reused as ore.

## 15. Two broken assets I nearly signed off, and the two prompt lessons behind them

The per-item audit went to zero flags, and then I went to look at the two renders I had produced but
never actually opened. Both assets were broken. **The audit was clean and the assets were wrong**,
which is the whole argument for the objective's "rendering it and looking at the render" half.

**`creature_bristleback_boar` did not reconstruct at all.** Its render was a tangle of flat grey
shards with no boar in it — no legs, no head, no silhouette. The concept was excellent: a proper wild
boar with tusks, snout, ears and a mane ridge. Two builds from that concept produced the same failure,
so it was reproducible rather than transient.

The difference between it and the four archetypes that work is the surface. The hound has a short
coat, the husk is bare bone, the armour is hard plate, the spider is smooth chitin — all solid,
high-contrast forms. The boar was the only one covered in **long, separate bristles**, and fine hair
strands are below the reconstructor's sampling resolution, so they come back as disconnected sheets.
The prompt now asks for a low ridge of short stiff hair on a solid matte hide, and the rebuild reads
unmistakably as a boar. It was then re-rigged — 18 bones, 0 unweighted vertices, exact match to its
clip skeleton — so its six animation clips still drive it.

**`resource_ash_haft` was a completed spear.** The bible wants a raw crafting material — the stave the
March Spear is built from — and the asset had a metal spearhead, a binding collar and a butt cap. The
fault was mine in two ways. The prompt said "a haft stave **for a spear**", and it also said "**no
metal, no binding, no head**" — and naming the forbidden parts is what summons them; the generator
drew all three. Rewritten to describe only a bare shaft, never mentioning a spear, a head or metal, it
produced exactly the pale knot-marked stave that was wanted.

Both failures are one lesson from opposite directions: **describe what you want to see, and describe a
surface the reconstructor can resolve.** A negative instruction does not subtract the thing it names,
and fine hair is not geometry the pipeline can hold.

A third, smaller thing: `_build_bible_batch.py` skipped the ash haft because its skip test compares
the built size against the declared size, and both said 1.90 m. It has no way to notice that the
*concept* behind that size was replaced, so a corrected concept needs `_make_assets.py --force`
directly. Worth knowing before trusting a "nothing to build" line.

## 16. What was verified, and how

| Claim | Evidence |
|---|---|
| Scale corrections applied | Catalog rebuilt; `PASS_SCALE` 233 → 242; bounds re-measured per file |
| Rigged creatures now correct | Accessor bounds identical to their `ready/` meshes; 18 bones; 0 unweighted vertices |
| Meshes survived re-rigging intact | Turnarounds rendered and looked at for the hound and the spider |
| 30 creature clips exist and bind | Rebuilt against the new rigs with 0 bones missing; clips are animation-only |
| VFX retargeted | Review sheets composited on dark at the declared blend mode, then inspected |
| Strain overlay is a real vignette | Measured: centre alpha 0.0, symmetric edges, corners 211 |
| Landmark sizes declared | 15 rules written, and verified through the resolver rather than assumed — the first version was silently ignored on every `landmark_` id |
| The waystone is a usable landmark | Rebuilt at 2.8 m, rendered and looked at; was a 0.5 m barrel-sized mesh |
| The buildings are real buildings | Measured against declared footprints (6.0 × 6.3, 9.0 × 6.3) **and** rendered with the material stripped, which shows four walls, a gable and a door gap |
| Buildings can batch | Materials collapsed from 38 per building to 7 — one per distinct piece |
| Nothing in the animation library is orphaned | All 85 rigged assets matched bone-for-bone against a clip skeleton; 0 unmatched |
| All five archetypes are distinct | Each rendered at corrected exposure and looked at individually |
| The bristleback boar actually exists | Its first two builds rendered as flat shards and were found only by opening the render, after the per-item audit had already gone green. Rebuilt from a fur-free concept and looked at; re-rigged with 0 unweighted vertices and an exact clip match |
| The ash haft is a raw material | Its concept was a finished spear; corrected prompt, rebuilt, rendered and confirmed as a bare pale stave |
| The per-item audit is honest | `_audit_objective.py` reports 0 missing and 0 open flags, and it reports `no-lod` as information rather than a defect because the kit itself carries no LODs |
| The preview exposure was wrong | Measured against a known material, corrected, and the correction validated by re-rendering before trusting it |

## 17. What is not done

1. **The four NPCs are animation-capable but have no NPC-specific motion.** They bind exactly to the
   `creature_humanoid_20` skeleton and can reuse the husk's and armour's twelve clips for idle, walk,
   run, attack, hit and death today. What does not exist is talking, handing something over, working
   at the forge or the survey table, or sitting. Nor were they renamed — the bible calls those names
   working names, so renaming would be premature.
2. **The NPC skeleton may not be the canonical one.** The canonical doc says 24-bone; the NPCs carry
   20. Worth resolving before armour production rather than after.
3. **`creature_dune_jackal_scout` is still 1.800 m against an expected 0.90 m** — the same
   category-default problem as the Kal, now visible because the audit's expectations are honest. It
   is outside this batch but it is a creature the player may meet.
4. **`resource_iron_ore` is the wrong object and should not be reused as ore.** It is a stylised
   hexagonal crystal in a white frame, not a rock. Nothing consumes it, so it is harmless where it
   sits, but it is a trap for anyone searching the library for an ore asset — `item_raw_iron_ore` is
   the canonical one.
5. **The Charwood woodland was verified by sample, not in full.** Four of the twenty tree and flora
   assets were rendered and looked at — oak, pine, birch and fern, all correct. The other sixteen are
   assumed sound on the strength of the sample rather than confirmed.
6. **295 other concept images have no built asset.** They are outside this batch; a folder-wide
   `--skip-existing` build would take several hours on ASTRAL and was deliberately not started.
7. **No world materials or terrain work.** The bible's geography (cells, road spline, 10 m of
   elevation) is level design, not asset work, and no terrain exists yet.
8. **Quest-marker and interaction-socket wiring** for the new landmarks is not done; they are meshes.
9. **The buildings' materials need an art pass.** They are structurally correct and batch correctly,
   but the kit pieces are flat and untextured-looking where the bible wants weathered frontier
   timber. This was previously misdiagnosed as a broken material binding; it is not, and the fix is
   art direction rather than plumbing.
10. **Godot validation is per-mesh, not per-assembly.** The validator reports the first mesh in a GLB,
    so a 28-piece building is confirmed to *import* cleanly but is not confirmed piece by piece.
    Worth knowing before treating a green result as whole-asset coverage.
11. **The remaining 51 `FAIL_SCALE` and 127 `SUSPECT_SCALE` assets are untouched.** They are outside
    this batch, but they are the same class of defect the Kal were, and the race fix just proved the
    audit can find them once the expectations are honest.

## 18. Audio feedback from the owner

Recorded here so it is not lost, since it arrived with this instruction:

> "Some of these sounds are fine, but there's a lot I'm not sure really even make sense... None of
> the animal sounds sound like an animal, just ticks and light thunk sounds."

That matches what the spectrograms showed and what the sprint status already flagged: the creature
sets read as broadband noise washes with periodic striations rather than animal vocalisations — a
breath shape never appeared for `player.hurt.light` either. The likely cause is prompt framing plus
Stable Audio Open's difficulty with short articulatory sounds, and the fix is a prompt-level
regeneration rather than a pipeline change. **The four creature families are the first thing to
audition when the owner listens.**

---

## Ownership

Touched: `assets/manifests/semantic_dimensions.json`, `assets/manifests/magic_vfx.json`,
`assets/rigged/creature_{ash_ember_hound,bristleback_boar,cave_hunting_spider}/`,
`assets/ready/{7 rescaled assets}/`, `assets/vfx/**`, `assets/animation/{clips,ready/creatures}/`,
`assets/requests/phase1_{bible_landmarks,hud_icons}.json`, `assets/_superseded/**`,
`assets/review/**`, and the tools named above.

Not touched: any gameplay, spell, AI, inventory or quest system. No ComfyUI core source.
