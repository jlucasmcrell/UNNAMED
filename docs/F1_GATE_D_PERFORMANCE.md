# Gate D parts 2–3: animations and face

Branch `npc/gate-d`, base `78c2f3d3` from `codex/f1-siann-faces`.
Own checkout: `D:\Otherreach_Scratch\npc_gate_d`. Gate C's checkout is read-only to this lane.

Status: IN PROGRESS. Neither part is FINISHED or ready for visual acceptance.

The shared native face keeps blinks and saccades and now accepts authored dialogue `mood`
(`neutral`, `warm`, `curious`, `worried`, `stern`, `sad`, `surprised`). Existing unannotated
lines get restrained text direction. Conversation views carry that direction. Ambient
voiced barks supply the speaker's playback clock to the same text-viseme driver as dialogue.
The jaw amplitude uses per-recording 20ms envelopes where staged (all 75 existing recordings
prepared), with the existing voice-bus fallback for recordings without a sidecar. Facial jaw/eye binding also accepts native rigs extended by
hair/cloth bones. The creator's FACE tab previews expressions without changing identity.

NPC work motion is selected from its real schedule activity and role; unavailable clips
are not substituted with an unrelated work take. Sleep/seating retain furniture support.
Source expansion starts from these catalog rows in `asset_library/RECONCILED/ANIMATION_LIBRARIES.md`:

- `ANIM-e4b3381d0a46`: Quaternius UAL1.
- `ANIM-3b794dbb1160`: Quaternius UAL2.
- `ANIM-db979793fbdf`: FreeAnimationLibrary.
- `ANIM-7d2eed57d3f5`: Mixamo acquired library.
- `ANIM-948eb573a956`: Rokoko free library.
- `ANIM-74d0b7a7bb52`: CMU library, still to extend beyond the existing bow source.

Initial UAL/FAL pass exported 138 states per native sex target. These are intermediate
transports, not a promoted runtime library. Full Mixamo/Rokoko source preparation runs on
CPU, retaining native rigs and keys. Original source bones get marker triangles only for
Epic's skeletal import. The final target pass snapshots and probes the current creator
bodies, which have changed since the older target reports. Native rest/hierarchy audits
remain mandatory before packaging a library.

Minimal checks so far: Presentation build succeeds; focused CharacterPerformanceTests and
authored-mood Content check pass. No GPU run or visual acceptance claimed.

One opt-in no-HUD proof route is `OTHERREACH_GATE_D=animation|face` through the existing
RCD3 review launcher. Animation requires the smith's actual schedule to be working and the
native `work_smith` clip to be active. Face opens a normal Sel conversation, then shows
the creator's expression previews. Timestamped frames retain actual playback timing.
The clips have not yet been captured or inspected.

D15 hooks: existing Ashen Hollow smith/cook/resident schedules and conversations are the
world hooks. No new skill or technique is added. Hooks lint and dossier reconciliation
remain required before FINISHED. No new texture/LOD/VRAM savings introduced.
