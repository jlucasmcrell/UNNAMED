# NPC idle and speech presentation

Based on `claude/pp11-fixes`. F1 owns face identities, skin/eyes/hair and
`GentleRest`; this lane changes body idle selection, relaxed fingers and speech.
No body, mesh, rest transform, skin bind or authored identity weights change.

## Idle sources and delivery

The installed pool has four distinct performances: UAL1 `Idle_Loop`, UAL2
`Idle_No_Loop`, UAL2 `Idle_FoldArms_Loop`, and CMU `77_02` (standing).
The old `idle_look` alias remains compatible but is not counted
as variety and is not selected when the new observation is installed.
The `idle_observe` state is CMU's stationary `77_02`; the searching crouch in
`77_01` was rejected during private source inspection.
CMU `140_07` passed numeric invariants but its crouched stance failed the
in-game style review. It is retained only as negative proof, never installed.

CMU provenance: <https://mocap.cs.cmu.edu/search.php?subjectnumber=77> and
<https://mocap.cs.cmu.edu/search.php?subjectnumber=140>. Source BVHs use the
existing Shriinivas/cmubvh distribution and existing CMU credit. The existing
Blender BVH importer/glTF exporter supplies source bone markers only. Export
starts at frame 3, omitting the conversion's artificial calibration pose and
its first following sample. Epic IK Retargeter normalizes the entire source
performance's facing through its native additive pose operation. No arm keys
are authored; fingers and facial joints remain excluded from body retargeting.

Exact-target native glTF transports pass the existing rest/finger guards.
`merge_humgen_idle_motion.gd` adds only `idle_observe` to each exact
body library, reloads it, and compares all previous animation keys, paths,
interpolation, loop settings and metadata. Existing locomotion/combat stays
unchanged. Install the external motion delivery with the code; never overwrite
accepted payload hardlinks in place. No KayKit animation is permitted.

Each NPC retains a deterministic identity-based starting phase and independent
activity timing. Activities finish before returning to base idle; consecutive
activities differ when alternatives exist. Missing optional additions retain
the old compatible pool. Armed/furniture/combat poses retain their existing
prop-safe selection rules.

## Hands and speech

Empty native hands use 0.32 of the existing anatomical grip curl, previously
0.20. Held-weapon full curl and authored grip paths are unchanged. This is an
additive pose modifier, never a skeleton rest edit.

Speech sends 55% of the former envelope to the common native jaw/lip FACS
weight; teeth follow the existing native hinge curve. Bilabial visemes close
the lips and cap the jaw, and existing vowel/consonant FACS continue to drive
the lips. Speech channels use approximately 56 ms attack / 100 ms release;
blinks, resting expressions and gaze retain their existing smoothing. Small
speech brow changes and a bounded 1.43-degree nod accompany articulation.
Text-timed visemes remain an approximation, not phoneme-aligned acting.

## Proof

Private review uses three actual named NPCs with adjacent initial positions
and routine anchors within the waystation faction seat. It runs normal idle
selection for twenty real seconds, then a normal voiced Renn greeting with
the actual Master bus and native jaw readback. The review draws only its three
NPC subjects (the simulation population remains intact); the foreground player
is hidden for the review camera. This is not a world or performance gate.
No authoritative state writes, forced mouth
weights, animation clock override, public package change or promotion.

Proof root: `D:/Otherreach_Scratch/npc_life_20261027`.
Visual acceptance remains with Claude after original footage is inspected.
