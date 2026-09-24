# NPC-Specific Animation: the gap, and what is safe to author

**Date:** 2026-09-24
**Status:** gap documented; a small shared proof set is authorised, mass authoring is not

## What exists today

All four Phase-1 NPCs are rigged, weighted (`0 unweighted vertices` each) and animation-capable.
Locomotion, attack, hit and death come free from the shared clip family, so each NPC walks, runs,
fights and dies correctly as it stands.

## What is missing

The behaviours that make an NPC read as a person rather than a body:

| Missing | Who needs it |
|---|---|
| Talk / conversation gestures | all four |
| Hand-over (giving an item, taking payment) | all four, and the crafting chain |
| Working at the forge | Kera Voss |
| Working at the survey table | Sel Arienn |
| Sitting / social idles | all four, for the waystation interior |

## Why this was not authored immediately

Because the animation targets a skeleton whose status was ambiguous. The NPC rigs carry **20 bones**
and the canonical humanoid carries **52** with a 24-bone core, and the two do not share joint names:
`hips` vs `pelvis`, `upper_arm.L` vs `upperarm_l`, one `spine` vs `spine_01/02/03`. Authoring social
animation against the wrong one would either need redoing or would quietly lock the NPCs out of the
canonical armour path.

That is now resolved in `docs/SKELETON_CONTRACT_RECONCILIATION.md`, which establishes three distinct
contracts and concludes that mass re-rigging is **not** required for M6.

## What is authorised now

The brief permits a **small shared NPC social/work proof set**, on two conditions:

1. it targets the **stable simplified NPC skeleton** (the 20-bone rig), and
2. it does **not create future armour lock-in**.

Both conditions are satisfied by keeping the proof set to gestures that are *bones-only and
skeleton-local*: they animate the 20-bone rig's own joints and reference no canonical bone, no
armour attachment and no equipment socket. That means the clips remain valid whichever way the NPC
skeleton question is settled later - either they keep playing on the 20-bone rig, or they are re-cut
against the canonical rig from the same authored poses, which are recorded as joint angles rather
than as baked bone-name bindings.

## The proof set to author

Deliberately small, and shared rather than per-NPC, because the four NPCs have the same body:

| Clip | Frames | Purpose |
|---|---|---|
| `npc_social_idle_a` | 96 | standing conversational idle |
| `npc_social_talk_a` | 72 | talking gesture, one hand raised |
| `npc_social_talk_b` | 72 | talking gesture, both hands open |
| `npc_handover_a` | 48 | offering an object with the right hand |
| `npc_work_forge_a` | 96 | striking at a forge with both arms |
| `npc_work_table_a` | 96 | leaning over a table, both hands forward |
| `npc_sit_idle_a` | 96 | seated social idle |

Seven clips, one shared set, applied to whichever NPC needs them by name.

## Hard boundaries

- **No per-NPC bespoke animation in this pass.** Four NPCs times five behaviours is twenty clips
  nobody has asked for yet.
- **No new bones, no rig changes.** If a gesture needs a joint the 20-bone rig does not have, the
  gesture is out of scope rather than a reason to extend the skeleton.
- **No gameplay-code changes.** These are clips and metadata; wiring them to dialogue or a work
  station is Claude's to do.
- **Sitting requires a seat height**, which is level design. The seated idle is authored to a
  documented seat height and the assumption is recorded rather than left implicit.

## Blocked on the owner

Nothing here is blocked. The proof set above is authorable now, and the only decision that would
change it - moving NPCs onto the canonical skeleton - is a stop-and-ask item under the maintenance
brief and is not being started.
