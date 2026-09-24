# NPC-Specific Animation: the gap, and what is safe to author

**Date:** 2026-09-24
**Status:** proof set **built and verified**; mass authoring still not done, and not needed

## What exists today

All four Phase-1 NPCs are rigged, weighted (`0 unweighted vertices` each) and animation-capable.
Locomotion, attack, hit and death come free from the shared clip family, so each NPC walks, runs,
fights and dies correctly as it stands.

## The proof set was built (2026-09-24)

Five shared clips, `anim.npc.*`, in `assets/animation/ready/npc/`:

| clip | category | type | loop | duration |
|---|---|---|---|---|
| `anim.npc.talk` | social | emote | yes | 3.20 s |
| `anim.npc.handover` | interaction | interact | no | 1.33 s |
| `anim.npc.work_forge` | profession | emote | yes | 1.60 s |
| `anim.npc.work_table` | profession | emote | yes | 3.00 s |
| `anim.npc.sit` | social | idle | yes | 4.00 s |

**Verified, not asserted:**

- **70/70 clips in the whole animation library verify** (`_verify_animation_glb.py --all`), including
  these five: animation-only (`meshes=0`), 20 bones, 60 channels.
- **Each clip binds to all four NPCs.** They carry bone-for-bone identical 20-bone skeletons -
  `npc_veth_magistrate`, `npc_kal_smith`, `npc_siann_archivist` and `npc_orenth_guide` all match
  exactly - so one shared set serves all four rather than twenty per-NPC clips.
- **Loops are sealed**: loop delta 0.0004-0.0008, against a 0.001 threshold. `handover` is one-shot and
  correctly reports no loop delta.
- **Poses were looked at**, rendered on an actual NPC rig. Talk gesticulates with one hand raised,
  hand-over reaches forward, work-forge leans and drives both arms down, work-table leans with both
  hands out, sit lowers the hips with thighs level.

### Why it cannot cause armour lock-in

The clips animate only bones the simplified rig already owns, and reference **no canonical bone, no
armour attachment and no equipment socket**. The authored motion is joint angles rather than baked
bone-name bindings, so if the skeleton question is settled the other way the same poses can be re-cut
against the canonical rig without re-authoring anything. Tavar and the player were **not** migrated
onto an equippable rig, and no armour work was started.

### A rendering trap this exposed

`_render_anim_preview.py` renders against the **canonical fit body**, whose bones are named
`upperarm_l`, `calf_l`, `pelvis`. The NPC clips animate `upper_arm.L`, `shin.L`, `hips`. The retargeter
therefore found nothing to drive and **all four social clips rendered as identical A-poses** - which
reads as four broken clips and is actually one wrong body. It is
`docs/SKELETON_CONTRACT_RECONCILIATION.md`'s name mismatch, surfacing as a rendering fault.

`_render_npc_preview.py` renders on a 20-bone NPC rig instead, reusing `_blender_anim_creature`'s own
`import_rigged` and `apply_motion`, so the preview is applied by the same code that exports the clips.
Reported `keyed=10..15 missing=0` for all five.

### The seated pose has a garment artefact

`sit` folds the legs correctly, but the NPC's robe was modelled for a standing figure and deforms
oddly when the thighs come up. That is a garment and skinning concern, not a clip fault, and it is
recorded here rather than fixed by distorting the pose to suit the clothing.

### Known limitation

`_make_anim_state_machine.py` and `_verify_animation_glb.py` both searched only `humanoid`,
`creatures` and `mechanical` under `ready/`, so a clip in a new family folder was reported as missing
rather than unlisted. Both now include `npc`. The same shape of gap would hit the next new family.

## What is still missing

| Missing | Who needs it |
|---|---|
| Per-NPC bespoke behaviour | nobody yet; the shared set covers the Phase-1 need |
| Facial animation | out of scope; no facial rig on the simplified skeleton |
| Conversation-specific beats (nod, shrug, point) | would extend the same set if a quest needs them |
| The seated pose on a seated *garment* | a modelling task, not animation |

## Hard boundaries still in force

- **No per-NPC bespoke animation.** Four NPCs times five behaviours is twenty clips nobody has asked
  for; five shared clips do the job.
- **No new bones, no rig changes.** If a gesture needs a joint the 20-bone rig does not have, the
  gesture is out of scope rather than a reason to extend the skeleton.
- **No gameplay-code changes.** These are clips and metadata; wiring them to dialogue or a work
  station is Claude's.
- **Sitting assumes a seat at roughly 0.45 m** above the floor for a 1.80 m NPC. That is level design,
  recorded rather than left implicit.

## Blocked on the owner

Nothing. The proof set is authorable and authored; the only decision that would change it - moving
NPCs onto the canonical skeleton - is a stop-and-ask item and was not started.
