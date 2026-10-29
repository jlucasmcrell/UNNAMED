# Gate D parts 2–3: animations and face

Branch `npc/gate-d`, base `78c2f3d3` from `codex/f1-siann-faces`.
Own checkout: `D:\Otherreach_Scratch\npc_gate_d`. Gate C's checkout is read-only to this lane.

Status: IN PROGRESS. Neither part is FINISHED or ready for visual acceptance.
Gate C snapshot `e3fcae86` is integrated on this lane as merge `0064d926`; its own checkout is untouched.

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

The first face proof job is submitted to the 6900XT farm (`f1_gate_d_face_20261029_01`), using a private
copied art tree and a newly created character named Eris. Its results are pending. Existing in-game
credits already cover these motion sources under AN01–AN06; no new licence claim is introduced.

The initial source auto-characterizer produced empty chains for Mixamo/HumanIK prefixes. The numeric
motion audit caught static smith/cook exports. Explicit source chains corrected them, preserving the
native target; audited hand displacement is about 1.17/1.12m with ~0.002mm rest error. These checks are
transport evidence, not a substitute for the owed working clip. All 2,548 owned CMU takes now have
source-only preview transports; a sampled CMU import and native retarget pass the rest/motion check.
The remaining source batch and full library packaging are still running/pending.

The source scan is complete: 600 distinct Mixamo/Rokoko files, including six face-only
ARKit takes. Those facial channels are preserved at their original 100Hz timing and can
play on the native face from the creator's motion preview. Body source imports also include
the 38 acquired mobility FBXs, all 2,548 CMU takes and the native humanoid catalog packs.
The expanded Fab project migration found 688 takes; separate ActorCore, MCO/death and
Hyper humanoid sources supplement it. Full imports/retargets are still in progress.
Source mannequin corrective calibration and creature/weapon-only rigs are distinguished
in the source inventory. CMU subject 13 trial 23 is the original sweeping-floor motion:
https://mocap.cs.cmu.edu/search.php?subjectnumber=13&trinum=23 .

Named player gestures are offered in Chat; movement/combat interrupt them. Idle pools
follow role and mood. Native sleep motion uses the existing furniture support. Kera's work
anchor now sits within reach of the existing anvil, and the existing Old Mine hammer is
mounted at its authored-size wooden haft during the smith take. These need rendered review.

The first farm package stopped before rendering because the private copy had overwritten
the world texture-cache manifest with the smaller creator manifest. They are now merged.
Native creator body/face cache entries retain source resolution; the separate native outfit
entries are being refreshed through the usual headless cache builder. The broad retained-tree
cache scan also reports older world mip entries, which are outside this character change;
it is not reported as a package acceptance pass. No source texture has been reduced.
The staging helper now carries native facial-performance JSON alongside the motion resources.

D15 hooks: existing Ashen Hollow smith/cook/resident schedules and conversations are the
world hooks. No new skill or technique is added. Hooks lint and dossier reconciliation
remain required before FINISHED. No new texture/LOD/VRAM savings introduced.
