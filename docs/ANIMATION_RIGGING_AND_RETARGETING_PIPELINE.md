# OTHERREACH — Animation, Rigging & Retargeting Pipeline

**Status:** Working production design  
**Project:** Otherreach  
**Repository codename:** UNNAMED  
**Purpose:** Define a scalable animation pipeline that can support a large number of humanoid races, creatures, modular weapons, first-person combat, and procedural/world interactions without requiring hand-authored animation for every asset.

---

# 1. Core Production Principle

> **Otherreach should reuse motion through canonical skeleton families, retargeting, additive layers, IK, and procedural correction rather than creating a unique animation set for every character and every weapon.**

Blender can automate most of the repetitive production work:

- import;
- skeleton mapping;
- rig cleanup;
- retargeting;
- root-motion handling;
- foot correction;
- clip trimming;
- loop cleanup;
- animation baking;
- naming;
- metadata generation;
- export;
- validation.

However, Blender does not invent high-quality gameplay motion by itself.

The motion still needs to originate from one or more sources:

- purchased animation libraries;
- motion capture;
- video-to-motion systems;
- AI-generated motion;
- procedural animation;
- hand-keyed animation;
- physics-assisted cleanup;
- custom mechanical animation.

The pipeline therefore separates **motion creation** from **motion normalization and delivery**.

---

# 2. Pipeline Overview

```text
SOURCE MOTION
purchased / mocap / video-to-motion / AI / hand-keyed / procedural
        │
        ▼
SOURCE SKELETON
        │
        ▼
RETARGET MAP
        │
        ▼
CANONICAL OTHERREACH SKELETON FAMILY
        │
        ▼
AUTOMATED BLENDER PROCESSING
scale / orientation / root / feet / hands / trim / loop / bake
        │
        ▼
ANIMATION QA
        │
        ▼
ANIMATION-ONLY GLB
        │
        ▼
GODOT ANIMATION LIBRARY
        │
        ▼
AnimationTree / BlendSpace / StateMachine / IK / additive layers
        │
        ▼
GAMEPLAY
```

The canonical skeleton is the stable contract.

Source motions are disposable/interchangeable.

---

# 3. Animation Is Separate From Character Mesh Production

A character asset should not own its own duplicate copies of every common animation.

Prefer:

```text
Character Mesh + Skeleton
        +
Shared Animation Library
```

rather than:

```text
Character_A.glb
    walk
    run
    attack
    block
    idle
    death

Character_B.glb
    walk
    run
    attack
    block
    idle
    death

Character_C.glb
    ...
```

Shared animation libraries reduce:

- disk duplication;
- memory duplication;
- update complexity;
- retargeting maintenance;
- iteration cost.

A corrected walk animation should be replaceable once for all compatible characters.

---

# 4. Canonical Skeleton Families

Do not attempt to support every race with one universal skeleton if anatomy differs materially.

Initial working families:

```text
humanoid_standard
kal_compact_winged
vaskaal_tall_articulated
ondrek_heavy
constructed_standard
mor_special
```

These names are production taxonomy and need not become player-visible gameplay enums.

---

# 5. `humanoid_standard`

Likely used by:

- Veth;
- Siann;
- Orenth;
- many ordinary human-like NPCs;
- possibly some Constructed configurations.

This should be the most compatible with existing humanoid animation libraries.

Recommended core bones include:

```text
root
pelvis

spine_01
spine_02
spine_03
chest
neck
head

clavicle_l
upperarm_l
lowerarm_l
hand_l

clavicle_r
upperarm_r
lowerarm_r
hand_r

thigh_l
calf_l
foot_l
toe_l

thigh_r
calf_r
foot_r
toe_r
```

Plus standardized:

- finger bones;
- weapon attachment points;
- hand IK targets;
- foot IK targets;
- look/aim helpers;
- optional twist bones;
- optional facial rig.

Exact naming should be frozen before large-scale animation production.

---

# 6. Kal Skeleton

Kal need a separate family because of:

- shorter/broader proportions;
- dense mass;
- large folding dorsal wings;
- armor wing channels;
- wing-assisted movement.

The Kal rig should support at least:

```text
standard humanoid core
+
wing_root_l
wing_mid_l
wing_tip_l

wing_root_r
wing_mid_r
wing_tip_r
```

The final wing hierarchy may require more segments.

Wing states:

- folded;
- partially spread;
- braking;
- assisted jump;
- controlled descent;
- full deployment;
- damage/injury states.

Humanoid upper-body combat motion may still be retargeted onto Kal, but wing and locomotion layers require species-specific handling.

---

# 7. Vaskaal Skeleton

Vaskaal require dedicated validation because of:

- extreme height;
- narrow proportions;
- unusual forearm articulation;
- unusual ankle articulation;
- 3-primary-finger + 2-opposing-thumb hands;
- potentially different gait mechanics.

A normal humanoid rig may be usable as the underlying structure, but production should expect:

- different limb proportions;
- extra articulation bones;
- species-specific hand rig;
- custom gait cleanup;
- weapon grip adjustments.

Do not generate an entirely separate weapon catalog for Vaskaal.

Instead:

> **Shared weapon components + Vaskaal-compatible grip/interface family.**

The animation system should follow the same principle.

---

# 8. Ondrek Skeleton

Ondrek should not move like ordinary humans despite potentially sharing a broadly humanoid layout.

Their visual identity suggests:

- heavy center of mass;
- slower acceleration;
- deliberate changes of direction;
- asymmetrical mineral growth;
- reduced soft-tissue motion;
- possibly irregular joint ranges.

A canonical Ondrek rig should allow reuse of humanoid actions while preserving species-specific motion through:

- retarget scaling;
- timing adjustments;
- additive weight/mass layers;
- species-specific locomotion;
- procedural foot placement.

---

# 9. Constructed Skeleton

Baseline Constructed can likely use a near-standard humanoid rig.

Additional needs:

- extremely clean/mechanical pose control;
- optional reconfiguration;
- transformation bones;
- later optional secondary manipulators/arms;
- deployable tools;
- integrated mechanisms.

Do not make four arms mandatory in the baseline skeleton unless the production design settles that direction.

Prefer optional extension rigs or modular appendage bones.

---

# 10. Mor Skeleton

Mor should be treated as a special-case presentation family.

Ordinary foot-planted humanoid animation is probably inappropriate.

Possible approach:

```text
humanoid semantic pose skeleton
+
procedural drift
+
lower-body dissolve/deformation
+
floating root motion
+
phase distortion
```

The rig exists to preserve readable:

- gesture;
- combat intent;
- dialogue;
- casting;
- interaction.

But locomotion can be substantially procedural.

---

# 11. Creature Skeleton Families

Humanoid retargeting solves only part of the game.

Create reusable creature rig families where practical:

```text
quadruped_small
quadruped_large
avian
winged_biped
serpentine
multi_leg_small
multi_leg_large
arthropod
floating_entity
```

Do not assume one quadruped motion set works for every creature.

Reuse is appropriate where:

- joint topology is compatible;
- gait is similar;
- proportions are not extreme.

Species-specific cleanup remains expected.

---

# 12. Animation Wave 0

Before producing a large animation library, run an explicit **Animation Wave 0**.

The goal is to prove the entire pipeline.

## Recommended proof character

Use one revised Veth character on the finalized `humanoid_standard` skeleton.

## Minimum proof clips

Produce/import:

1. idle;
2. walk;
3. run;
4. sprint;
5. one-handed sword attack;
6. spear thrust;
7. bow shot;
8. block;
9. hit reaction;
10. downed/death;
11. simple interaction animation;
12. basic casting gesture.

## Required retarget proof

Retarget the same compatible library onto:

- Veth;
- Siann;
- Orenth.

Then test at least one clip on:

- Kal;
- Vaskaal;

to identify where generic retargeting stops being acceptable.

## Required Godot proof

Demonstrate:

- animation-only GLB import;
- shared `AnimationLibrary`;
- locomotion state machine;
- 1D or 2D locomotion blend;
- attack transition;
- upper-body layer;
- weapon grip IK;
- root-motion test;
- one additive injury/aim layer.

Animation Wave 0 does not pass until the entire chain works inside Godot.

---

# 13. Motion Sources

Otherreach should use multiple motion sources.

No one tool should become a production dependency unless it demonstrates repeatable quality.

## 13.1 Purchased animation libraries

Best for broad baseline coverage:

- locomotion;
- generic combat;
- climbing;
- gathering;
- common interactions;
- hit reactions;
- social idles.

Advantages:

- inexpensive compared with hand animation;
- professionally authored;
- large clip counts;
- often already looped.

Disadvantages:

- style inconsistency;
- skeleton differences;
- exaggerated motion;
- animation may not match Otherreach's combat philosophy.

Purchased assets should be treated as **motion source material**, not sacred final animation.

---

# 14. Video-to-Motion / AI Mocap

This is particularly useful for Otherreach.

A human can perform:

- hammer swing;
- spear thrust;
- pickaxe action;
- mining;
- blacksmithing;
- injured walk;
- bow preparation;
- ritual gesture;
- climbing motion.

Video-to-motion converts the performance to skeletal animation.

Then Blender can:

- retarget;
- clean;
- stabilize;
- bake;
- export.

This can turn ordinary phone video into useful production motion.

---

# 15. Hand-Keyed Animation

Still appropriate for:

- signature combat moves;
- race-specific locomotion;
- supernatural movement;
- hero/boss moments;
- unusual creatures;
- transformation;
- cinematic interactions;
- poses that AI/mocap repeatedly fails.

Hand animation should be reserved for places where it adds disproportionate value.

---

# 16. Procedural Animation

Use procedural systems for:

- look-at;
- aim offsets;
- head tracking;
- hand placement;
- foot placement;
- recoil;
- breathing;
- weapon alignment;
- terrain adaptation;
- additive injury;
- wing stabilization;
- Mor drift;
- idle variation.

Procedural animation is especially valuable because it reduces the number of stored clips.

---

# 17. Blender Automation Responsibilities

DeepSeek or another automation agent should eventually drive scripts that perform the deterministic parts of the pipeline.

Suggested sequence:

```text
1. import source FBX/BVH/GLB
2. identify source skeleton
3. map source bones
4. retarget to canonical skeleton
5. normalize scale
6. normalize axis/orientation
7. identify animation range
8. remove garbage lead/trailing frames
9. correct root
10. correct floor height
11. validate foot contacts
12. validate hand position
13. trim
14. loop-fix if applicable
15. bake animation
16. remove source rig
17. rename animation
18. emit metadata
19. export animation-only GLB
20. run structural checks
```

Anything deterministic should be scripted.

---

# 18. Root Motion

Otherreach should support both:

## Code-driven locomotion

Best for:

- ordinary walking;
- running;
- sprinting;
- strafing;
- general exploration.

Gameplay movement controls velocity while animation matches the requested speed.

## Root-motion actions

Useful for:

- lunges;
- leaps;
- vaults;
- finishing actions;
- special dodges;
- takedowns;
- cinematic interactions.

Do not force one global strategy.

Animation metadata should declare whether a clip contains authoritative root motion.

Example:

```yaml
root_motion:
  enabled: true
  axes:
    translation_xz: true
    translation_y: false
    rotation_y: true
```

---

# 19. Animation Metadata

Every animation clip should eventually have metadata such as:

```yaml
animation_id: anim.humanoid.polearm.thrust_01
skeleton_family: humanoid_standard

category: combat
animation_family: polearm
clip_type: attack

loop: false
root_motion: true

duration_s: 0.82

events:
  - id: attack_commit
    time: 0.21

  - id: hit_window_start
    time: 0.29

  - id: hit_window_end
    time: 0.47

  - id: recovery_start
    time: 0.52

hand_profile:
  left: grip_secondary
  right: grip_primary

tags:
  - two_hand
  - thrust
  - forward_commit
```

The exact schema is future work.

The important point is that animation should not depend on hidden clip naming conventions alone.

---

# 20. Gameplay Events Inside Animation

Combat needs explicit event timing.

Examples:

- weapon becomes dangerous;
- projectile release;
- shield block active;
- foot leaves ground;
- transformation lock completes;
- spell release;
- sound event;
- effect event.

Do not make gameplay logic depend purely on “frame 17” hard-coded in a script.

Use animation event metadata.

---

# 21. Weapon Animation Families

Do **not** create a unique animation library per weapon item.

Use broad families.

Initial candidates:

```text
unarmed
dagger
one_hand_blade
one_hand_blunt
two_hand_blade
two_hand_blunt
polearm
spear
shield
bow
crossbow
throwing
magic_focus
dual_wield
```

Some may merge later.

A weapon definition references:

- animation family;
- primary grip;
- secondary grip;
- stance;
- reach class;
- hand usage;
- optional special actions.

---

# 22. Grip Metadata

Weapon sockets and animation metadata should share a common interface.

Example:

```text
SOCK_grip_primary
SOCK_grip_secondary
```

The character animation establishes the approximate posture.

IK aligns hands with actual grip sockets.

This allows a single spear animation family to support many spear lengths and configurations.

---

# 23. Inverse Kinematics

IK is required for modular equipment.

Use it for:

- second-hand grip;
- bow hand/string;
- hand on staff;
- shield alignment;
- tool handles;
- door handles;
- interaction points;
- foot terrain contact.

Retargeting provides the broad body motion.

IK provides exact local alignment.

---

# 24. Additive Animation Layers

Avoid creating complete clips for every combination.

Prefer:

```text
base locomotion
+
upper-body weapon pose
+
aim offset
+
injury layer
+
look-at
+
procedural hands
+
species layer
```

Examples:

Instead of:

```text
walk_normal
walk_injured
walk_injured_cold
walk_injured_cold_torch
walk_injured_cold_torch_looking_left
```

use layered composition.

This is critical for keeping the library manageable.

---

# 25. Locomotion Blending

Typical locomotion system may use:

- idle;
- forward walk/run;
- backward;
- lateral;
- diagonal;
- sprint;
- crouch;
- injured variants.

Blend by:

- velocity;
- direction;
- stance;
- injury state;
- weapon posture.

Do not require discrete clips for every exact movement vector if a blend space works.

---

# 26. First-Person Animation Strategy

Otherreach is first-person and therefore has a special presentation problem.

## Option A — Full-body first person

The player camera exists on the actual character.

Advantages:

- consistent world body;
- correct shadow;
- fewer duplicate rigs.

Risks:

- hands/weapons can look bad close to camera;
- head/shoulders can obstruct view;
- third-person motions can feel wrong from eye position.

## Option B — Separate FPS viewmodel

Dedicated arms/hands/weapons rendered for the player.

Advantages:

- best weapon feel;
- precise framing;
- easy to exaggerate readable motion.

Costs:

- duplicate presentation;
- separate animation workload;
- potential mismatch between world-body and viewmodel.

## Recommended working direction

> **Hybrid full-body authority + first-person presentation layer where needed.**

Keep the real body in the world.

Use first-person-specific arm/weapon presentation for actions that otherwise look poor near the camera.

Prove this during combat prototyping before committing to a huge FPS-specific library.

---

# 27. First-Person vs World Animation

For some actions, the player may need:

```text
world animation:
humanoid.polearm.thrust_01

first-person presentation:
fps.polearm.thrust_01
```

Both represent the same authoritative action.

Gameplay timing comes from the shared action/system.

The first-person animation must not alter hit rules independently.

---

# 28. Transforming Weapons

Transforming equipment uses two animation layers:

## Mechanical transform

Bones/objects animate:

- telescoping shaft;
- deployed blade;
- rotating head;
- sliding rail;
- locking ring;
- chamber;
- aperture.

## Character transition

The character changes from:

```text
magic_focus stance
    ↓
deploy action
    ↓
polearm stance
```

or similar.

Mechanical state and gameplay state must stay synchronized.

---

# 29. Mechanical Animation Metadata

Transforming item definition may include:

```yaml
mechanism:
  type: telescoping
  animation: anim.weapon.staff_spear.deploy
  duration_s: 0.65
  state_from: focus
  state_to: polearm
  commit_event: mechanism_locked
```

The system changes gameplay mode only when the defined commit event occurs.

---

# 30. Armor and Animation

Armor must be validated against movement.

At minimum test:

- neutral;
- walk;
- run;
- arms raised;
- cross-body reach;
- crouch;
- overhead strike;
- deep leg flexion.

Race-specific tests:

### Kal
- wings folded;
- half-deployed;
- deployed.

### Vaskaal
- full forearm articulation;
- ankle range;
- hand grip.

Armor that looks correct only in bind pose is not production-ready.

---

# 31. Cloth / Capes / Hair

Prefer simulation or secondary-motion systems for:

- capes;
- cloth;
- long hair;
- hanging straps;
- tassels.

Do not bake unique cloth motion into every base animation unless necessary.

Physics should be constrained enough to avoid unstable gameplay behavior.

---

# 32. Facial Animation

Do not block early gameplay development on sophisticated facial animation.

Possible staged path:

## Early
- blink;
- jaw;
- simple emotion presets;
- basic head/eye tracking.

## Later
- phoneme/viseme support;
- expressive face rig;
- procedural emotion;
- optional generated dialogue lipsync.

Major authored scenes can receive higher-quality face work later.

---

# 33. Interaction Animation

Interactions should rely on target anchors.

Examples:

```text
SOCK_interact_handle
SOCK_interact_chest_lid
SOCK_interact_workbench
SOCK_interact_anvil
SOCK_interact_mount
```

The character aligns to a known anchor, then plays an interaction family.

This prevents needing custom animation for every door/chest/bench.

---

# 34. Animation Source Asset Lineage

Preserve provenance.

Example:

```text
source video / purchased clip / AI motion
        ↓
raw source animation
        ↓
retargeted Blender working file
        ↓
canonical baked animation
        ↓
animation-only GLB
        ↓
Godot imported library
```

Do not overwrite the only copy of the source motion.

---

# 35. Suggested Folder Layout

```text
assets/
  animation/
    source/
      purchased/
      mocap/
      ai/
      video/
      manual/

    rigs/
      humanoid_standard/
      kal_compact_winged/
      vaskaal_tall_articulated/
      ondrek_heavy/
      constructed_standard/
      mor_special/

    blender/
      work/
      canonical/

    ready/
      humanoid/
      creatures/
      first_person/
      mechanical/

    manifests/
    review/
    rejected/
```

Generated/heavy outputs should follow repository ignore policy.

---

# 36. Naming Convention

Example:

```text
anim.humanoid.locomotion.walk_forward
anim.humanoid.locomotion.run_forward
anim.humanoid.sword.light_01
anim.humanoid.polearm.thrust_01
anim.humanoid.bow.release_01

anim.kal.locomotion.glide_brake
anim.kal.wing.deploy

anim.vaskaal.locomotion.walk
anim.mor.locomotion.drift

anim.weapon.telescoping_staff.deploy

anim.fps.sword.light_01
```

The exact syntax should be reconciled with repository DefinitionId rules.

---

# 37. Automatic QA

Each processed clip should receive structural checks.

Possible checks:

- expected skeleton family;
- no unexpected bones;
- no missing required bones;
- root exists;
- no giant scale change;
- clip length sane;
- root height sane;
- foot penetration below tolerance;
- hand error below tolerance when grip constrained;
- no NaN transforms;
- no unbounded rotation;
- loop seam under threshold when `loop=true`;
- animation name/ID valid;
- export succeeded;
- Godot import succeeded.

---

# 38. Visual QA

Automation should render standardized previews.

For every clip:

- front;
- side;
- 3/4;
- optional first-person preview.

For locomotion:

- floor grid;
- root path;
- foot-contact markers.

For weapon animation:

- actual reference weapon;
- primary/secondary grip markers;
- hit-window visualization.

For armor testing:

- representative pose suite.

---

# 39. QA Classification

Do not use only PASS/FAIL.

Suggested states:

```text
PASS
FAIL_RETARGET
FAIL_SCALE
FAIL_ROOT
FAIL_FOOT_SLIDE
FAIL_HAND_ALIGNMENT
FAIL_CLIPPING
FAIL_LOOP
FAIL_TIMING
FAIL_SOURCE_MOTION
FAIL_EXPORT
FAIL_GODOT_IMPORT
NEEDS_MANUAL_REVIEW
```

This enables automated routing.

---

# 40. Failure Handling

When a clip fails, the system should decide whether the problem originates in:

1. source motion;
2. source skeleton;
3. retarget mapping;
4. proportions;
5. IK;
6. root extraction;
7. Blender cleanup;
8. export;
9. Godot import;
10. gameplay timing.

Do not regenerate the motion blindly before identifying failure class.

This mirrors the successful lesson from the current modular-asset Wave 0.

---

# 41. AI / Model Role Separation

A major production lesson is:

> **Cheap models are excellent for volume; expensive models should be reserved for decisions where one bad choice contaminates hundreds of downstream assets.**

Do not make one inexpensive vision model responsible for:

- architecture;
- production-standard design;
- exception handling;
- pipeline debugging;
- aesthetic approval;
- batch supervision;
- final QA.

Split roles.

---

# 42. Recommended Model Roles

## Tier 1 — Cheap batch worker / vision inspector

Appropriate tasks:

- inspect preview renders;
- classify obvious defects;
- compare against checklist;
- generate routine metadata;
- identify visible clipping;
- detect missing limbs/items;
- categorize likely failure type;
- summarize batches;
- run repetitive automation.

DeepSeek V4 Flash Vision can remain useful here if cost/performance is favorable.

This role should not be trusted to silently alter pipeline standards.

## Tier 2 — Strong control/reasoning model

Use for:

- interpreting failures;
- changing Blender automation;
- modifying canonical rig rules;
- approving new pipeline standards;
- deciding whether a new skeleton family is required;
- resolving inconsistent QA;
- reviewing hard edge cases;
- planning regeneration.

This is where a stronger cloud model can save substantial time despite higher token cost.

## Tier 3 — Human approval

Reserve for:

- race identity;
- hero assets;
- animation feel;
- stylistic authenticity;
- combat readability;
- major pipeline changes;
- anything that would trigger mass regeneration.

---

# 43. Escalation Pipeline

Recommended control loop:

```text
cheap model performs batch
        ↓
automated checks
        ↓
cheap vision model reviews standardized preview
        ↓
PASS ───────────────→ ready
        │
        └─ uncertain / repeat failure
                 ↓
        strong reasoning model
                 ↓
        modify pipeline or classify exception
                 ↓
        human only when needed
```

This preserves the cost advantage of cheap models without letting their mistakes compound.

---

# 44. Confidence Thresholds

Batch automation should use explicit confidence policy.

Example:

```text
high confidence obvious pass
    → auto-accept

high confidence known failure
    → auto-route for regeneration/fix

uncertain
    → strong model review

pipeline-standard conflict
    → strong model + human review
```

Never allow an uncertain vision classification to silently rewrite canonical production rules.

---

# 45. Model Change Does Not Require Pipeline Change

The production pipeline should remain model-neutral.

A generated asset/motion should be defined by:

- inputs;
- outputs;
- metadata;
- QA criteria.

Not by:

> “DeepSeek always does this.”

This makes it easy to swap:

- DeepSeek;
- Claude;
- local Qwen;
- future vision model;
- manual operator;

without rebuilding the workflow.

---

# 46. Automation Agent Safety

Any agent controlling Blender in bulk should operate under constraints.

It may:

- import;
- process;
- create derived files;
- export;
- classify;
- move rejected outputs.

It should not casually:

- overwrite canonical body rigs;
- replace approved skeleton standards;
- delete source motion;
- mass-regenerate accepted production assets;
- alter naming/interface contracts.

Those require explicit approval.

---

# 47. Standardized Preview Harness

Create one Blender preview scene per skeleton family containing:

- neutral floor;
- scale markers;
- neutral lighting;
- reference weapon;
- grip markers;
- camera positions;
- optional skeleton overlay.

Every animation should render through the same harness.

This allows reliable vision-model comparison.

---

# 48. Animation Family Proof Before Bulk Generation

For each new animation family:

1. generate/import 3–5 representative clips;
2. retarget;
3. validate;
4. test in Godot;
5. verify gameplay timing;
6. approve family;
7. then expand.

Do not generate 150 polearm clips before proving five.

---

# 49. Versioning

Version:

- canonical skeleton;
- retarget map;
- animation processing pipeline;
- animation metadata schema.

Example:

```text
skeleton_version: humanoid_standard_v1
retarget_profile_version: human_src_v3
animation_pipeline_version: 1.2
```

If a skeleton changes incompatibly, old animations must be reproducibly reprocessed.

---

# 50. Canonical Skeleton Change Policy

Changing bone names/orientation after hundreds of clips exist is expensive.

Therefore freeze:

- hierarchy;
- bone naming;
- coordinate conventions;
- base pose;
- required bones;
- root behavior;

as early as practical.

Optional extension bones may evolve without invalidating the base contract.

---

# 51. Reference Pose

Choose one canonical source/reference pose.

Likely:

- T-pose or A-pose for binding;
- explicit forward axis;
- exact unit scale;
- feet on ground plane.

Every character family needs a documented reference pose.

Retarget scripts should normalize to it.

---

# 52. World Scale

Animation and skeleton scale must align with the existing Wave-0 asset standard:

- meters;
- known world scale;
- transforms applied;
- consistent forward/up axes;
- ground reference.

Never normalize character dimensions by arbitrary longest-axis scaling.

Character height and limb dimensions are semantic.

---

# 53. Combat Timing and Animation

Animation must not determine combat outcomes by visual guess alone.

Combat systems should define:

- windup;
- commitment;
- active window;
- recovery.

Animation expresses those phases.

A clip must expose appropriate events/normalized timing.

This allows:

- balancing without reauthoring everything;
- animation replacement;
- slowed/hastened variants;
- species adjustment.

---

# 54. Animation Speed Changes

Allow bounded playback scaling for:

- skill;
- weapon mass;
- fatigue;
- injury;
- magical effects.

Avoid extreme time scaling that destroys motion quality.

At large differences, use a separate animation variant.

---

# 55. Procedural Aim

Ranged weapons and magic benefit from layered aiming.

Base motion:

```text
bow draw
```

then runtime adjusts:

- torso;
- shoulders;
- head;
- arms;
- weapon.

This reduces the need to author one animation per vertical/horizontal aim angle.

---

# 56. Hit Reactions

Hit reactions should account for:

- direction;
- body region;
- severity;
- stagger;
- weapon type.

Use a compact library + procedural selection/blending rather than hundreds of bespoke reactions.

Downed/death states should remain system-driven.

---

# 57. Traversal

Potential future families:

- jump;
- mantle;
- vault;
- climb;
- ladder;
- swim;
- crouch;
- crawl;
- squeeze;
- mount/dismount.

Prove only the traversal needed for the first region/vertical slice.

---

# 58. Noncombat Profession Animation

Reusable categories include:

```text
hammer_workbench
hammer_anvil
saw
chop
mine
dig
harvest
skin
mix
write
read
inspect
carry
push
pull
lift
```

Interaction anchors + IK should let the same base motion serve many world objects.

---

# 59. NPC Animation Diversity

Avoid cloning one exact walk across everyone.

Variation can come from:

- stride scale;
- speed;
- posture layer;
- idle set;
- injury;
- age;
- load;
- personality;
- race;
- environment.

Small procedural differences create much more perceived diversity than hundreds of almost-identical clips.

---

# 60. Weather / Environment Integration

The animation system will eventually need modifiers for:

- strong wind;
- slippery ground;
- snow;
- mud;
- cold;
- heavy rain;
- steep slope;
- deep water.

Do not create unique full-body clips for every weather condition.

Prefer:

- locomotion parameter changes;
- additive posture;
- procedural balance;
- speed/traction adjustments.

---

# 61. Soul / Supernatural Animation Hooks

Potential future presentation layers:

- Soul disturbance;
- possession;
- Othermarking;
- phase instability;
- divine influence;
- resurrection.

These should be layered where possible instead of replacing the entire animation library.

---

# 62. Animation Performance

Large NPC populations require careful runtime cost.

Potential strategies:

- animation LOD;
- lower update rates at distance;
- reduced bone sets;
- pose sharing/caching;
- no facial animation at distance;
- simplified IK at distance;
- baked/cheap distant locomotion.

These must align with simulation/render tiers but remain presentation-only optimizations.

---

# 63. Animation LOD

Possible presentation tiers:

## Near
Full skeleton, IK, additive layers, face.

## Medium
Full locomotion/combat, reduced IK/face.

## Far
Reduced skeleton/update rate.

## Very far
No skeletal presentation because actor is outside visual simulation.

Presentation LOD must never change authoritative gameplay results.

---

# 64. Godot Integration Contract

Animation delivery should eventually standardize:

- skeleton profile mapping;
- animation-only GLB import;
- shared libraries;
- `AnimationTree`;
- state machines;
- blend spaces;
- root-motion extraction;
- event callbacks;
- IK/procedural modifiers.

Do not bind authoritative combat logic directly to arbitrary imported clip names.

Use stable animation IDs/metadata.

---

# 65. First Production Milestone After Wave 0

Once Animation Wave 0 passes, create only the animation needed for the first playable loop.

Likely:

## Locomotion
- idle;
- walk;
- run;
- sprint;
- turn;
- crouch if required.

## Combat
- one-hand weapon;
- polearm;
- bow;
- block;
- hit;
- downed/death.

## World interaction
- pick up;
- open;
- gather;
- workbench.

## Magic
- one basic cast family.

Do not produce the entire future animation catalog before gameplay is proven.

---

# 66. Relationship to Current Asset Wave 0

The animation effort should not derail the current modular-asset Wave 0.

Recommended order:

1. finish current modular mesh/interface proof;
2. freeze sufficient character scale/body-reference conventions;
3. establish canonical skeleton v1;
4. run Animation Wave 0;
5. only then expand animation library in parallel with gameplay development.

The rigging/animation pipeline depends on stable body and equipment contracts.

---

# 67. Recommended Immediate Actions

When the current mesh Wave 0 is stable:

1. select final representative Veth body;
2. create `humanoid_standard_v1`;
3. document axes, scale, bind pose and bone names;
4. rig Veth;
5. rig/retarget Siann and Orenth;
6. obtain a small source motion library;
7. create Blender retarget/export automation;
8. export animation-only GLBs;
9. build Godot locomotion/combat proof;
10. add hand IK to one modular weapon;
11. record all failures;
12. only then choose the next animation source/model/tool.

---

# 68. Production Decision About AI Models

The current cheap vision model can continue doing valuable work.

The correct reaction to hiccups is not necessarily:

> replace it everywhere.

Use it where errors are inexpensive and detectable.

Move **control-plane decisions** to a stronger model:

- canonical rig design;
- Blender automation changes;
- interpretation of repeated failures;
- approval of new asset families;
- production-standard changes.

Keep **data-plane batch work** inexpensive:

- hundreds of inspections;
- metadata;
- batch classification;
- routine renders;
- standard regeneration.

This should substantially reduce cost without letting cheap-model mistakes propagate through the entire asset library.

---

# 69. Final Production Rule

> **Automate repetition. Standardize interfaces. Retarget aggressively. Use IK and additive layers to absorb variation. Spend manual or expensive-model effort only where motion quality or pipeline decisions genuinely require it.**

That is the animation equivalent of Otherreach's modular crafting and asset philosophy.
