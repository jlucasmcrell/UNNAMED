You are the Asset Production Lead, Technical Artist, Animation Pipeline Lead, and Blender Automation Engineer for Otherreach.

Project root:
G:\UNNAMED

Your assignment has CHANGED.

STOP the current backlog-clearing / broad asset-generation task after leaving it in a safe resumable state.

Do NOT throw away useful work.
Do NOT delete source concepts or unfinished pipeline experiments.
Do NOT continue generating assets merely because they are next in a queue.

The new priority is:

============================================================
MAKE OTHERREACH PLAYABLE
============================================================

We already have enough raw assets.

The bottleneck is no longer asset quantity.

The bottleneck is converting a deliberately small subset into a COMPLETE GAMEPLAY-READY SET with:

- correct real-world scale
- canonical rigs
- animation
- sockets
- collisions
- LOD
- materials
- Godot validation
- gameplay-useful metadata
- coherent environment coverage

The current inventory already contains approximately:

- 455 built assets
- 750 concepts
- 62 creatures
- 106 weapons
- 40 magic/ritual props
- 36 travel assets
- 82 rigged assets
- 15/15 Wave-0 proof assets

That is enough raw material for the first playable game.

DO NOT optimize for a larger “assets built” number.

Optimize for:

> HOW MANY COMPLETE GAMEPLAY LOOPS CAN THE ASSET LIBRARY SUPPORT?

============================================================
0. FIRST: STOP CURRENT WORK SAFELY
============================================================

Before changing direction:

1. Inspect current Git status.
2. Record exactly what task you were doing.
3. Record:
   - files currently modified
   - generated outputs not yet catalogued
   - any host jobs currently running
   - any animation pipeline work partially complete
   - exact commands needed to resume later
4. Do NOT discard unfinished work unless it is clearly disposable generated output.
5. Create/update:

`docs/PLAYABLE_ASSET_SPRINT_STATUS.md`

with a section:

## Deferred Work Checkpoint

describing what was paused and how to resume it.

Do NOT commit or modify Claude-owned gameplay/domain code.

Your ownership during this sprint is:

- assets/
- asset-pipeline tooling
- Blender automation
- rigging
- animation
- asset metadata/manifests
- Godot asset-import/validation scenes where appropriate
- asset-production documentation

Do NOT edit gameplay/domain/application systems unless specifically asked later.

============================================================
1. READ BEFORE WORKING
============================================================

Read the current versions of:

- WAVE_0_ASSET_INVENTORY_REPORT.md
- WAVE_0_MODULAR_ASSET_STANDARD.md
- WAVE_0_PROOF_RESULTS.md
- WAVE_0_NAMING_AND_HOST_ALLOCATION.md
- ANIMATION_RIGGING_AND_RETARGETING_PIPELINE.md
- CANONICAL_BODY_AND_SKELETON.md
- ANIMATION_METADATA_SCHEMA.md
- RACES.md
- RACES_UPDATE_NOTES.md
- COMBAT_DAMAGE_ARMOR_AND_DEATH.md
- CRAFTING_AND_ITEMIZATION.md
- CAMERA_PERSPECTIVE_AND_PRESENTATION.md
- PROTOTYPE.md
- ROADMAP.md
- IMPLEMENTATION_PRECEDENCE_AND_DESIGN_STATUS.md

Also inspect:

`assets/catalog.json`

Treat the latest repository docs as authority.

Do not restore older race anatomy or first-person-only assumptions from stale documents.

============================================================
2. MOST IMPORTANT FINDING: FIX SEMANTIC SCALE
============================================================

The inventory shows that many older assets still appear to use the old category-normalization behavior rather than believable absolute dimensions.

Examples include assets that appear approximately:

- dagger: ~1.2 m
- throwing knife: ~1.2 m
- door: ~0.5 m
- tree: ~0.5 m
- ship: ~0.5 m

That is unacceptable for gameplay.

Wave 0 established the principle:

> ABSOLUTE SEMANTIC SIZE IS AUTHORITATIVE.
> DO NOT NORMALIZE MODULAR OR GAMEPLAY ASSETS BY LONGEST AXIS.

This is now the highest-priority asset cleanup.

DO NOT immediately rescale all 455 assets manually.

Instead:

A. Build an automated semantic-scale audit.

B. Define or consume per-asset/per-family expected physical dimensions.

C. Produce a report classifying every asset:

- PASS_SCALE
- SUSPECT_SCALE
- FAIL_SCALE
- UNKNOWN_SCALE

D. Fix the PLAYABLE SUBSET first.

E. Only migrate the wider library after the playable subset is proven.

The script/report should detect obvious absurdities such as:

- dagger taller than a person
- door too small for canonical humanoid
- tree smaller than player
- horse/ship/cart at prop scale
- oversized handheld potion
- unusably small building fixture

Prefer explicit semantic dimensions over guessed category scaling.

============================================================
3. FREEZE A PLAYABLE-ASSET MANIFEST
============================================================

Create:

`assets/manifests/playable_prototype_assets.json`

This is the small set Claude/gameplay work should rely on through M6.

It should list ONLY the assets intentionally chosen for the first playable prototype.

Every listed asset needs status fields for:

- asset_id
- category
- source concept
- canonical scale
- fit/skeleton family if applicable
- ready GLB
- rigged GLB if applicable
- animation state
- LOD state
- collision state
- socket/interface state
- Godot import validation
- gameplay role
- known limitations

The manifest is more important than clearing the global queue.

============================================================
4. PLAYER CHARACTER — P0
============================================================

Deliver ONE complete production-usable player character first.

Use:

**Veth**

unless current repository docs explicitly choose another Phase-1 player body.

Requirements:

- current revised Veth biology
- correct semantic height/proportions
- canonical humanoid skeleton
- clean skinning
- stable root
- hand bones
- finger bones sufficient for weapon grip
- foot bones
- required IK targets/helpers
- equipment attachment sockets
- world collision/body-reference dimensions
- Godot import validation

The full-body character is authoritative.

Otherreach is now:

> third-person / over-the-shoulder primary with seamless zoom into first person

Do NOT optimize the rig solely for first-person arms.

============================================================
5. ONE COMPLETE WEARABLE OUTFIT — P0/P1
============================================================

Do NOT build the entire planned 90-piece armor system yet.

Build ONE coherent Veth-compatible prototype outfit.

Minimum:

- underlayer / torso garment
- chest protection
- leg garment/protection
- boots
- gloves
- optional gorget/neck
- optional helmet if useful

Requirements:

- correct canonical fit
- survives locomotion
- survives attack poses
- survives crouch
- survives overhead arm movement
- no catastrophic clipping
- reasonable skinning
- correct materials
- metadata for covered anatomical regions

Remember:

> COMBAT COVERAGE REGIONS ARE METADATA.
> THEY DO NOT REQUIRE ONE MESH SLOT PER REGION.

Use the proven Method C canonical-fit workflow where appropriate.

Do not return to manual surface surgery as a production method.

============================================================
6. ANIMATION WAVE 0 IS NOW CRITICAL PATH ASSET WORK
============================================================

Animation is now more valuable than generating new static meshes.

Complete the Animation Wave 0 pipeline.

For the player, minimum useful clips:

LOCOMOTION
- idle
- walk
- run
- sprint
- turn / directional support as needed

GENERAL
- basic interact
- pickup/reach if needed
- hit reaction
- downed/death

COMBAT
- sword ready/idle
- sword attack
- sword block/parry if prototype needs it
- polearm ready/idle
- polearm thrust
- bow ready
- bow draw/release

MAGIC
- one generic casting gesture suitable for the M3e proof

The goal is NOT final AAA animation.

The goal is:

> reliable gameplay-capable motion through the complete Blender → GLB → Godot pipeline.

Required proof:

- canonical skeleton
- retarget
- animation-only GLB where appropriate
- AnimationLibrary compatibility
- loop validation
- root behavior
- hand/weapon IK compatibility
- Godot import

============================================================
7. THREE PROTOTYPE WEAPON FAMILIES ONLY
============================================================

Do NOT create more weapons.

We already have over 100.

Select the best existing geometry for:

1. one-handed sword
2. bow
3. polearm / spear

Suggested candidates may come from the existing inventory, but inspect geometry and choose the best production candidates rather than blindly following names.

Each chosen weapon must have:

- correct real-world dimensions
- correct forward axis
- grip socket(s)
- equip/attachment socket
- usable collision/reference geometry
- stable origin
- appropriate LOD
- material validation
- Godot validation

For two-handed weapons:

- primary grip
- secondary grip

must support runtime IK.

The weapon must work as a gameplay object, not merely look good in a render.

============================================================
8. DO NOT FINISH THE REMAINING WEAPON BACKLOG
============================================================

The single remaining unbuilt weapon concept is irrelevant to the prototype unless it is selected as one of the three proof families.

Do not generate it merely to achieve “107/107.”

Asset-count completion is not the goal.

============================================================
9. FIVE COMPLETE ENEMIES — NOT 62 HALF-FINISHED ONES
============================================================

Select EXACTLY FIVE existing creatures/enemies for the Phase-1 playable set.

Do not generate new creatures unless there is a critical missing role that cannot be filled by the 62 existing meshes.

Choose a set that:

- covers useful AI/combat behavior differences
- minimizes the number of completely unique rig pipelines
- still provides visual/gameplay variety

Recommended role coverage:

1. light humanoid/biped enemy
2. heavy humanoid/biped enemy
3. fast quadruped / pack enemy
4. heavy quadruped / brute
5. one distinctly nonhuman enemy

Possible existing candidates include things like:

- bone walker
- animated armour
- barrow wight/revenant
- wolf/hound
- boar
- cave spider
- clay servitor

but YOU should inspect the assets and choose the five with the best combination of:

- mesh quality
- topology
- rig readiness
- animation feasibility
- prototype role

For EACH enemy deliver:

- correct scale
- rig
- idle
- locomotion
- attack
- hit reaction
- death
- collision reference
- hit-region reference where practical
- Godot validation

Do not spend time polishing the other 57 yet.

============================================================
10. ENVIRONMENT KIT — CURRENT BIGGEST CONTENT GAP
============================================================

The inventory has many props but not enough coherent architecture for a playable settlement/world area.

Build a SMALL Otherhome-Marches prototype environment kit.

Do NOT build the eventual 70-building settlement library.

Minimum target:

STRUCTURES
- small cottage
- smith/workshop
- small inn/public building OR generic communal building
- simple ruin kit

MODULAR SUPPORT
- wall section
- roof section
- door frame + functional door
- window
- fence
- simple steps
- simple post/beam if needed

WORLD
- road/path treatment
- small bridge
- well
- signpost

All must use real human-scale dimensions.

A doorway should fit the canonical Veth body naturally.

============================================================
11. PROTOTYPE VEGETATION / ROCKS
============================================================

Do not generate new plant species.

We already have enough species diversity.

Take the best existing subset and make it production-useful:

- oak
- pine
- dead tree
- bramble
- fern
- heather/ground cover

Fix absolute scale.

Trees should be trees.

Create/validate:

- sensible LOD
- collision policy
- instancing compatibility
- materials
- Godot import

Also create/select a small rock set if the existing library lacks suitable region dressing.

We need a convincing 4-cell area, not botanical completeness.

============================================================
12. WORLD MATERIALS — MOVE THESE UP
============================================================

The inventory already contains ~60 rendered 2D world-material concepts.

These are now HIGH VALUE.

Prioritize a small production-ready set for the prototype environment.

Suggested first set:

- packed dirt
- gravel
- wet mud
- grass/marsh or forest ground
- oak/timber boards
- plaster/wattle-and-daub
- rubble stone
- dressed stone
- roof material
- metal/iron

Convert the best of these into validated tileable PBR materials.

Requirements:

- proper scale
- tiling
- normal/roughness consistency
- no visible baked lighting
- no obvious seams
- Godot material validation

Do NOT process all 60 before the first environment works.

============================================================
13. FIRST CRAFTING PROFESSION: BLACKSMITHING
============================================================

Prioritize blacksmithing because the current inventory already supports it unusually well.

Prototype gather→craft chain:

```text
iron resource node
    ↓
mining pick
    ↓
raw iron ore
    ↓
processed ingot / forge input
    ↓
anvil + hammer
    ↓
two simple recipes
```

Use existing assets where possible:

- mining pick
- ore
- ingot
- anvil
- blacksmith hammer
- tongs
- forge/bellows/workbench

Required assets/interactions should be correctly scaled and gameplay-ready.

Do NOT build a full profession ladder.

Do NOT build 16 herbs just because they are waiting.

============================================================
14. HERBS / ALCHEMY ARE NOT CURRENT PRIORITY
============================================================

Only one built herb is fine for now.

Leave the remaining herb backlog deferred unless Claude specifically needs one for a prototype test.

Blacksmithing is the first visual crafting loop.

============================================================
15. INTERACTION ASSETS — SMALL COMPLETE SET
============================================================

Prototype needs:

- door
- loot/storage chest
- resource node
- anvil/workbench
- simple pickup item
- maybe barrel/crate

Each interactive world object needs:

- correct scale
- sensible origin
- collision
- interaction anchor/socket
- animation anchor if required
- open/closed parts separated where appropriate
- Godot import proof

Do not build all remaining containers.

============================================================
16. MAGIC — STOP MAKING PROPS, START MAKING GAMEPLAY PRESENTATION
============================================================

We already have ~40 magic/ritual props.

That is enough.

For M3e, focus on presentation for THREE tiny representative magic proofs.

Coordinate with current magic design:

- Resonance / Strain
- no universal mana

Minimum visual set:

1. offensive cast effect
2. ward/protective effect
3. restorative OR utility effect

Need:

- casting animation
- hand/staff origin
- travel effect if projectile
- impact effect
- short-lived world effect where appropriate
- Strain visual feedback concept

Prefer Godot shader/particle-friendly assets.

Do not create another 30 ritual objects.

============================================================
17. MANA ASSETS ARE STALE
============================================================

The inventory contains:

- item_mana_potion
- item_mana_elixir_bottle concept

Otherreach no longer uses generic mana as the core magic resource.

Do NOT ship these under mana terminology.

Possible action:

- retain bottle mesh if useful
- rename/recontextualize later
- mark current mana-named assets:

STALE_DESIGN_NAME

Do not silently treat them as current canon.

============================================================
18. OLD DWARF / ELF NAMES ARE STALE
============================================================

The library still contains old generic names such as:

- dwarven_*
- elven_*

Otherreach has no stock fantasy dwarves/elves.

The geometry may remain useful.

Do NOT delete good meshes just for naming.

Instead classify them:

REUSABLE_GEOMETRY_NEEDS_RECONTEXTUALIZATION

Before they enter gameplay content:

- rename
- reassign cultural identity
- update metadata
- remove stale fantasy-race terminology

Do not let old concept-era names become canonical content IDs.

============================================================
19. NPC SET — SMALL AND CURRENT BIOLOGY ONLY
============================================================

We have eight representative NPC meshes.

Choose only 3–5 for the prototype settlement.

Likely roles needed:

- magistrate / authority
- smith
- scholar/archivist
- guide/traveler
- future companion candidate

Check every chosen NPC against CURRENT race morphology.

Do not use:

- wingless old Kal
- human-handed old Vaskaal
- outdated Siann
- outdated Ondrek

simply because those GLBs already exist.

If representative NPC meshes are stale, rebuild the chosen prototype NPCs from current canonical bodies/reference art.

============================================================
20. RACECLASS BACKLOG — CANCEL
============================================================

Do NOT build the 32 `raceclass_*` concepts now.

They are art-direction/costume boards.

They are not 32 required production characters.

Some combinations are conceptually stale anyway.

Leave them deferred.

============================================================
21. RACE2 BACKLOG — DO NOT BLINDLY BUILD
============================================================

Do NOT convert all 14 `race2_*` images to 3D.

Many are:

- hand studies
- eye studies
- wing states
- silhouette sheets
- motion references

Use them as design/reference inputs to canonical bodies and rigs.

Only build a `race2_*` reference if it represents geometry actually needed in gameplay.

============================================================
22. MOUNTS — DEFER
============================================================

All six mounts may remain unbuilt.

They are not required for the Phase-1 playable prototype.

Do not spend current render capacity on them.

============================================================
23. CONTAINERS — DEFER MOST
============================================================

Current built containers are sufficient for the prototype.

Prioritize:

- backpack if player presentation needs it
- chest
- barrel/crate
- belt pouch if useful

Leave the rest until logistics/storage gameplay actually needs them.

============================================================
24. UI — MINIMAL PLAYABLE SUBSET
============================================================

There are already 64 rendered UI icons.

Do NOT design the final UI.

Prepare only enough assets to support:

- Health
- Stamina
- Focus
- Resonance/Strain
- interaction prompt
- inventory/equipment
- sword
- bow
- polearm
- three proof spells
- essential statuses if required

Reuse existing icons where they fit.

Flag old mana/school terminology that no longer matches design.

============================================================
25. AUDIO — CHECK THE GAP
============================================================

The asset inventory report does not cover a comparable gameplay-audio set.

Inspect whether a separate audio library already exists.

If it does:
produce a small prototype-audio manifest.

If it does not:
create a gap report.

Minimum useful eventual set:

- footsteps
- sword swing
- melee impact
- bow draw/release
- creature attack
- creature death
- pickup
- interaction click
- spell cast
- spell impact
- forest/wind ambience
- small-settlement ambience

Do not block the 3D sprint waiting for polished audio.

Temporary/generated audio is acceptable.

============================================================
26. MACHINE ALLOCATION
============================================================

The owner reports three-machine rendering capacity is available.

However, verify actual current host capability before assigning jobs.

Do not assume an old host limitation is still true or resolved.

Record for each host:

- ComfyUI version
- Trellis availability
- VRAM
- current role
- safe output prefixes
- active workload

Continue avoiding host write collisions.

Use parallel rendering for ONLY the prototype-critical tasks first.

Do not use three GPUs to accelerate low-priority backlog.

============================================================
27. AUTOMATED PLAYABLE-ASSET VALIDATION
============================================================

Create or extend validation so every asset in:

`playable_prototype_assets.json`

must pass appropriate checks.

Possible checks:

STATIC
- file exists
- valid GLB
- transforms sane
- semantic scale sane
- required materials
- LOD present where required
- collision policy present
- stable asset ID
- provenance metadata

WEAPON
- primary grip socket
- secondary grip if required
- attachment socket
- correct dimensions

CHARACTER
- skeleton family
- expected required bones
- skin
- root
- import validation

ANIMATION
- clip ID valid
- target skeleton valid
- no NaN transforms
- root sane
- duration sane
- loops sane where required

INTERACTIVE PROP
- interaction anchor
- collision
- separated moving part if necessary

============================================================
28. STANDARDIZED GODOT VALIDATION SCENE
============================================================

Build/maintain an ASSET validation scene, not gameplay code.

The validation scene should be able to display:

- canonical Veth
- outfit
- selected weapons
- selected enemies
- environment kit
- prototype props
- animations

Useful views:

- scale reference
- humanoid reference
- animation playback
- weapon grip
- armor clipping pose
- LOD distance
- collision visualization

Do not implement gameplay systems in this scene.

============================================================
29. PROTOTYPE ASSET ACCEPTANCE TARGET
============================================================

The asset sprint is successful when this sentence is true:

> Claude can build M3–M6 without waiting for the asset pipeline to invent a basic player, enemy, weapon, building, material, interaction prop, or animation.

Minimum target bundle:

PLAYER
- 1 Veth production body
- 1 wearable outfit
- locomotion/general animations

COMBAT
- 1 sword
- 1 bow
- 1 polearm
- working grips/sockets/IK compatibility

ENEMIES
- 5 fully prepared enemies

NPC
- 3–5 settlement NPCs
- 1 companion-capable humanoid asset

WORLD
- 3–4 structures / modular building kits
- prototype foliage
- prototype rocks
- road/path
- bridge
- well/signpost
- 5–10 production PBR materials

INTERACTION
- door
- chest
- anvil/workbench
- resource node
- pickup item

CRAFTING
- pick
- ore
- ingot
- hammer/anvil
- visual support for 2 recipes

MAGIC
- 3 proof VFX
- cast animation

UI
- minimal icon subset

AUDIO
- minimal usable set OR clearly documented external gap

============================================================
30. DO NOT WAIT FOR PERFECT ART
============================================================

The goal is PLAYABLE.

Accept:

- temporary animations
- proof materials
- limited outfit variety
- one player race
- five enemies
- graybox support where needed

provided the interfaces are production-correct.

Do NOT spend days perfecting:

- decorative props
- distant regions
- mounts
- dozens of weapons
- every race costume
- every herb
- every UI icon

while the player still cannot walk, fight, loot and craft with the assets.

============================================================
31. OUTPUT DOCUMENTS
============================================================

Maintain:

`docs/PLAYABLE_ASSET_SPRINT_STATUS.md`

Create:

`docs/PLAYABLE_ASSET_GAP_REPORT.md`

and:

`assets/manifests/playable_prototype_assets.json`

The status doc must contain:

1. paused/deferred work
2. semantic-scale audit
3. prototype asset selections
4. animation state
5. character state
6. weapon state
7. enemy state
8. environment kit
9. crafting support
10. magic VFX
11. UI/audio gaps
12. Godot validation
13. blockers
14. next actions

============================================================
32. COMPLETION REPORT
============================================================

When the playable asset sprint reaches a meaningful checkpoint, report:

- assets selected
- assets rebuilt
- assets rescaled
- stale assets quarantined/recontextualized
- new environment assets created
- animations complete
- rig families used
- Godot validation result
- host jobs/run time
- remaining critical blockers
- exact files changed
- Git status
- commit(s), if owner-approved
- what Claude can now consume immediately

============================================================
33. FINAL PRIORITY RULE
============================================================

When deciding between:

A. generating another attractive asset

and

B. making an existing asset actually usable in gameplay

choose **B**.

When deciding between:

A. completing an entire asset family

and

B. completing the one member Claude needs for the prototype

choose **B**.

When deciding between:

A. adding variety

and

B. proving the full player → animation → weapon → enemy → environment → Godot chain

choose **B**.

============================================================
MISSION
============================================================

Your job is no longer:

> “Build the asset library.”

Your job is now:

> **“Deliver the smallest coherent production-quality asset subset that allows Otherreach to become a playable RPG.”**

Everything else can resume later.
