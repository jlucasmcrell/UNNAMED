# Phase B Remediation — Acceptance Contract (frozen 2026-09-26)

Frozen before further remediation, per the owner's priority order and the Codex forensic audit of the rejected baseline
(`G:/UNNAMED_HISTORY/CODEX_PHASEB_FORENSIC_AUDIT_2026-09-26/PHASE_B_FORENSIC_AUDIT.md`, baseline `22af661`). The audit and its
evidence are the BEFORE state; nothing here rewrites them.

## 1. The six gates

Every player-facing visual or animation state is reported against these gates, separately. A later gate never implies an
earlier one was checked, and an earlier gate never implies a later one.

| Gate | Meaning | Evidence that counts | Evidence that does NOT count |
|---|---|---|---|
| G1 EXISTS | The asset is on disk, licensed, with provenance | file + provenance/licence record | a catalogue row |
| G2 LOADS | The runtime loads it without fallback | boot log, ArtCoverage | — |
| G3 BOUND | A binding names it for the state | `art_bindings.json` (or code) reference | an AnimationLibrary entry; "all clips drawn" |
| G4 SELECTED | Normal gameplay's own selector chooses it (Main / NpcsView / SkinnedFigure state logic, real-time frame scheduling) | a frame-by-frame state log from a normal-game run, or a compiled-selector probe driven by real sequences | an AnimationSheet / showcase that calls the clip or SetDowned directly; an offline render |
| G5 DISPLAYED | In normal gameplay it looks right: no foot slide, no clipping, correct grip/contact, no snapping | VIDEO of continuous normal play (default High, normal camera); stills only for geometry/material | a pose sheet; a still of a moving state |
| G6 ACCEPTED | Owner or blind independent QA (Gemini + Pegasus, raw outputs kept) accepts it | review files under `external_reviews/`, owner sign-off | my own judgement alone |

Wording rule: a report says which gate a state reached ("sword attack 2: G4 selected, G5 filmed, G6 pending"), never
"shipped", "resolved" or "integrated" without the gate.

## 2. Geometry gates (characters, creatures, props)

Before any texture, material, lighting, LOD or animation acceptance:

1. untextured clay render, neutral lighting, bind pose: front, back, profile, neck/collar close-up from below;
2. wireframe of the same close-up;
3. no torn or horizontal lips at the head/neck/collar, no stretched panels, no holes, no fused finger blades where fingers
   are claimed;
4. then weight/pose deformation: the character QA poses (arms raised, arms forward, stride, crouch, twist) and the face checks
   (blink, jaw, speech shape, smile, frown, brows, eye look, neck turn/up/down/tilt).

A texture or material can never pass an asset whose clay fails.

## 3. Required normal-gameplay evidence for the checkpoint (VIDEO)

Default High, `people=production` (or the new standard's binding), normal camera, real-time frame scheduling
(`record_showcase.ps1`-style Movie Maker capture of a normal session, plus the frame-state log):

| State | Needed |
|---|---|
| Player idle, armed idle (sword ready) | G4 log + G5 video |
| Walk, run, sprint, crouch, start/stop, turning, blocked movement, slope | G4 log + G5 video; pace vs playback-rate table |
| Jump / fall / land | only if claimed in scope; else listed as remaining |
| Sword: ready, ≥3 distinct attacks, recovery, return to locomotion, grip | G4 log + G5 video |
| Spear, bow, magic | G4 log + G5 video (or explicitly remaining) |
| Mending Thread | normal-game cast video with camera distance/pitch/FOV/effect transform logged |
| Tavar rescue (Three Quiet Stones) | before/during/after video keyed to the existing quest states |
| Animated Armour | clay + gameplay-distance video, no label, ordinary light |
| Near grass / foliage | third-person, first-person, crouched stills + movement video |
| Visible equipment swap | video of equipping/unequipping the torso item |
| Companion (Tavar) movement | G4 state log (no run→idle→idle→run toggling) + video |

## 4. Audit register

The checkpoint answers each finding with FIXED / PARTIAL / NOT FIXED (or SUPERSEDED BY NEW PIPELINE for H01), citing the gate
evidence above:

H01 character geometry · H02 Animated Armour · H03 locomotion state coverage · H04 gait sampling/rates · H05 sword state/variety ·
H06 Mending · H07 old grass · H08 acceptance-gate weakness · M01 terrain composition · M02 trees · M03 Tavar presentation ·
M04 HUD · M05 inventory UX (inherited; coordinated with M7, not changed concurrently).

## 5. Preserved Phase B work (not to be discarded)

Terrain3D architecture and domain-height agreement, terrain layers, horizon/ravine/cliff dressing, Sky3D, texture cache,
material-zone and LOD infrastructure, retarget factory/provenance, foot-height conformance, head look, plant-model scatter, wind,
working ParticleRecipe callers, Fold/heart presentation, building-kit preparation, quality/performance/capture tooling, V3 audio
restraint.
